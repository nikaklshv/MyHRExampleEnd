using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace MyHRExample.Models
{
    public class CandidateModel
    {
        [Required(ErrorMessage = "Введіть ваше ім'я")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Введіть контакти")]
        public string Contacts { get; set; }

        [Required(ErrorMessage = "Завантажте резюме")]
        public IFormFile ResumeFile { get; set; }
    }
}