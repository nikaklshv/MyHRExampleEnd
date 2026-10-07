using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.AspNetCore.Mvc;
using MyHRExample.Models;
using MyHRExample.Services;
using System.Text.Json;
using UglyToad.PdfPig;

namespace MyHRExample.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly S3Service _s3Service;

        public HomeController(ILogger<HomeController> logger, S3Service s3Service)
        {
            _logger = logger;
            _s3Service = s3Service;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadText(string requirementText)
        {
            if (!string.IsNullOrEmpty(requirementText))
            {
                var key = $"requirements/text_{Guid.NewGuid()}.txt";

                await _s3Service.UploadTextContentAsync(key, requirementText);

                _logger.LogInformation($"Текст успішно збережено у файл з ключем: {key}");

                TempData["GeneratedKey"] = key;
                TempData["SuccessMessage"] = "Текст успішно збережено у файл та відправлено в S3!";
            }

            return RedirectToAction("Index");
        }


        [HttpGet]
        public IActionResult Candidate()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Candidate(CandidateModel model)
        {
            if (ModelState.IsValid && model.ResumeFile != null)
            {
                if (Path.GetExtension(model.ResumeFile.FileName).ToLower() != ".pdf")
                {
                    ModelState.AddModelError("ResumeFile", "Дозволені лише файли у форматі PDF.");
                    return View(model);
                }

                var uniqueKey = $"resumes/{Guid.NewGuid()}_{model.ResumeFile.FileName}";

                using (var stream = model.ResumeFile.OpenReadStream())
                {
                    var safeCandidateName = Uri.EscapeDataString(model.FullName);
                    await _s3Service.UploadResumeStreamAsync(uniqueKey, stream, safeCandidateName);
                }

                TempData["SuccessMessage"] = "Ваше резюме успішно надіслано!";
                return RedirectToAction("Candidate");
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> HrResumes()
        {
            var resumes = await _s3Service.GetResumesAsync();
            return View(resumes);
        }

        [HttpGet]
        public IActionResult DownloadResume(string key)
        {
            if (string.IsNullOrEmpty(key)) return BadRequest();

            var url = _s3Service.GetPresignedUrl(key, TimeSpan.FromMinutes(15));
            return Redirect(url);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteResume(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                await _s3Service.DeleteFileAsync(key);
                TempData["SuccessMessage"] = "Резюме успішно видалено.";
            }
            return RedirectToAction("HrResumes");
        }

        [HttpPost]
        public async Task<IActionResult> AnalyzeResume(string resumeKey, [FromServices] IConfiguration config)
        {
            string hrText = await _s3Service.GetLatestRequirementsAsync();

            string resumeText = "";
            using (var pdfStream = await _s3Service.GetFileStreamAsync(resumeKey))
            {
                using (var document = PdfDocument.Open(pdfStream))
                {
                    foreach (var page in document.GetPages())
                    {
                        resumeText += page.Text + " ";
                    }
                }
            }

            var apiKey = config["Gemini:ApiKey"];
            var client = new Client(null, null, apiKey);

            string prompt = $@"
                Ти професійний HR-менеджер. Проаналізуй резюме на основі вимог вакансії.
                Вимоги вакансії: {hrText}
                Резюме кандидата: {resumeText}

                ВАЖЛИВО: Надай відповідь ВИКЛЮЧНО у форматі валідного JSON (без форматування markdown типу ```json), за наступною структурою:
                {{
                  ""skills"": [""знайдена навичка 1"", ""знайдена навичка 2""],
                  ""confirmed_requirements"": [""вимога 1, яка є в резюме""],
                  ""missing_information"": [""вимога, якої немає в резюме""],
                  ""summary"": ""Короткий висновок: чи підходить кандидат.""
                }}";

            string jsonResult = "";
            int maxRetries = 5;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var response = await client.Models.GenerateContentAsync(
                        model: "gemini-3.8-flash", 
                        contents: prompt,
                        config: new GenerateContentConfig { Temperature = 0.1f }
                    );

                    jsonResult = response.Text;
                    jsonResult = jsonResult.Replace("```json", "").Replace("```", "").Trim();
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries) throw new Exception($"Справжня помилка Gemini: {ex.Message}");
                    await Task.Delay(4000);
                }
            }

            string jsonKey = resumeKey.Replace("resumes/", "analysis/").Replace(".pdf", ".json");
            await _s3Service.UploadTextContentAsync(jsonKey, jsonResult);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var analysisResult = JsonSerializer.Deserialize<AnalysisResultModel>(jsonResult, options);

            return View("ViewAnalysis", analysisResult);
        }
        [HttpGet]
        public async Task<IActionResult> ViewAnalysis(string resumeKey)
        {
            if (string.IsNullOrEmpty(resumeKey)) return BadRequest();

            string jsonKey = resumeKey.Replace("resumes/", "analysis/").Replace(".pdf", ".json");

            string jsonText = await _s3Service.GetTextFileAsync(jsonKey);

            if (string.IsNullOrEmpty(jsonText))
            {
                TempData["SuccessMessage"] = "Аналіз ще не знайдено або він у процесі.";
                return RedirectToAction("HrResumes");
            }

            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var analysisResult = System.Text.Json.JsonSerializer.Deserialize<MyHRExample.Models.AnalysisResultModel>(jsonText, options);

            return View(analysisResult);
        }
        [HttpGet]
        public async Task<IActionResult> HrAnalytics()
        {
            var resumes = await _s3Service.GetResumesAsync();

            var analyzedCandidates = resumes.Where(r => r.IsAnalyzed && r.Analysis != null).ToList();

            ViewBag.TotalCandidates = resumes.Count;
            ViewBag.AnalyzedCount = analyzedCandidates.Count;
            ViewBag.PendingCount = resumes.Count - analyzedCandidates.Count;

            var allSkills = analyzedCandidates
                .Where(r => r.Analysis.Skills != null)
                .SelectMany(r => r.Analysis.Skills)
                .ToList();

            var topSkills = allSkills
                .GroupBy(s => s)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .ToDictionary(g => g.Key, g => g.Count());

            ViewBag.SkillsLabels = System.Text.Json.JsonSerializer.Serialize(topSkills.Keys);
            ViewBag.SkillsData = System.Text.Json.JsonSerializer.Serialize(topSkills.Values);

            return View(analyzedCandidates);
        }
    }
}