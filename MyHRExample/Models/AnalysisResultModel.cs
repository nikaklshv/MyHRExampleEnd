using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace MyHRExample.Models
{
    public class AnalysisResultModel
    {
        [JsonPropertyName("skills")]
        public List<string> Skills { get; set; } = new List<string>();

        [JsonPropertyName("confirmed_requirements")]
        public List<string> ConfirmedRequirements { get; set; } = new List<string>();

        [JsonPropertyName("missing_information")]
        public List<string> MissingInformation { get; set; } = new List<string>();

        [JsonPropertyName("summary")]
        public string Summary { get; set; }
    }
}