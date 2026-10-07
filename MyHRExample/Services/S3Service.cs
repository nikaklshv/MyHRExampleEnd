using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace MyHRExample.Services 
{
    public class S3Service
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;

        public S3Service(IConfiguration config)
        {
            var accessKey = config["AWS:AccessKey"];
            var secretKey = config["AWS:SecretKey"];
            var region = RegionEndpoint.GetBySystemName(config["AWS:Region"] ?? "eu-central-1");
            _bucketName = config["AWS:BucketName"];

            _s3Client = new AmazonS3Client(accessKey, secretKey, region);
        }

        public async Task UploadFileStreamAsync(string key, Stream fileStream)
        {
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = fileStream
            };
            await _s3Client.PutObjectAsync(request);
        }

        public async Task UploadTextContentAsync(string key, string textContent)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(textContent));
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = stream
            };
            await _s3Client.PutObjectAsync(request);
        }

        public async Task UploadResumeStreamAsync(string key, Stream fileStream, string candidateName)
        {
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = fileStream,
                ContentType = "application/pdf"
            };

            request.Metadata.Add("candidate-name", candidateName);

            await _s3Client.PutObjectAsync(request);
        }

        public async Task<List<ResumeItemModel>> GetResumesAsync()
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = "resumes/"
            };

            var response = await _s3Client.ListObjectsV2Async(request);
            var resumes = new List<ResumeItemModel>();

            if (response.S3Objects != null)
            {
                foreach (var obj in response.S3Objects)
                {
                    if (obj.Key == "resumes/") continue;

                    string candidateName = "Невідомо";
                    try
                    {
                        var metadataResponse = await _s3Client.GetObjectMetadataAsync(_bucketName, obj.Key);
                        if (metadataResponse.Metadata.Keys.Contains("x-amz-meta-candidate-name"))
                        {
                            var name = metadataResponse.Metadata["x-amz-meta-candidate-name"];
                            if (!string.IsNullOrEmpty(name)) candidateName = Uri.UnescapeDataString(name);
                        }
                    }
                    catch { }

                    var resumeItem = new ResumeItemModel
                    {
                        Key = obj.Key,
                        FileName = Path.GetFileName(obj.Key),
                        CandidateName = candidateName,
                        UploadDate = obj.LastModified ?? DateTime.UtcNow
                    };

                    string jsonKey = obj.Key.Replace("resumes/", "analysis/").Replace(".pdf", ".json");
                    string jsonText = await GetTextFileAsync(jsonKey);

                    if (!string.IsNullOrEmpty(jsonText))
                    {
                        try
                        {
                            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            resumeItem.Analysis = System.Text.Json.JsonSerializer.Deserialize<MyHRExample.Models.AnalysisResultModel>(jsonText, options);
                            resumeItem.IsAnalyzed = true;
                        }
                        catch { resumeItem.IsAnalyzed = false; }
                    }
                    else
                    {
                        resumeItem.IsAnalyzed = false;
                    }

                    resumes.Add(resumeItem);
                }
            }
            return resumes;
        }

        public string GetPresignedUrl(string key, TimeSpan duration)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = key,
                Expires = DateTime.UtcNow.Add(duration)
            };
            return _s3Client.GetPreSignedURL(request);
        }

        public async Task DeleteFileAsync(string key)
        {
            var request = new DeleteObjectRequest { BucketName = _bucketName, Key = key };
            await _s3Client.DeleteObjectAsync(request);
        }

        public async Task<string> GetLatestRequirementsAsync()
        {
            var request = new ListObjectsV2Request { BucketName = _bucketName, Prefix = "requirements/" };
            var response = await _s3Client.ListObjectsV2Async(request);

            var latestObj = response.S3Objects
                .Where(o => o.Key.EndsWith(".txt"))
                .OrderByDescending(o => o.LastModified)
                .FirstOrDefault();

            if (latestObj == null) return "Вимоги відсутні.";

            using var getObjResponse = await _s3Client.GetObjectAsync(_bucketName, latestObj.Key);
            using var reader = new StreamReader(getObjResponse.ResponseStream);
            return await reader.ReadToEndAsync();
        }

        public async Task<Stream> GetFileStreamAsync(string key)
        {
            var response = await _s3Client.GetObjectAsync(_bucketName, key);
            var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }
        public async Task<string> GetTextFileAsync(string key)
        {
            try
            {
                using var response = await _s3Client.GetObjectAsync(_bucketName, key);
                using var reader = new StreamReader(response.ResponseStream);
                return await reader.ReadToEndAsync();
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null; 
            }
        }
    }



    public class ResumeItemModel
    {
        public string Key { get; set; }
        public string FileName { get; set; }
        public string CandidateName { get; set; }
        public DateTime UploadDate { get; set; }
        public bool IsAnalyzed { get; set; }
        public MyHRExample.Models.AnalysisResultModel Analysis { get; set; }
    }



}