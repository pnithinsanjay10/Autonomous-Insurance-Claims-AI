using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class AiExtractionService
{
    private readonly string _apiKey;

    public AiExtractionService(string apiKey)
    {
        _apiKey = apiKey;
    }

    public async Task<string> ExtractAsync(string text)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

        var prompt = $@"
You are an insurance claim data extractor specializing in ACORD forms.

The document is an ACORD Automobile Loss Notice form. Extract the following information carefully from this structured form.

IMPORTANT: Look for these SPECIFIC field locations in the ACORD form:

1. POLICY NUMBER - Look for the field labeled ""POLICY NUMBER"" (it appears near the top right section with CARRIER, NAIC CODE)
2. POLICYHOLDER NAME - Look for ""NAME OF INSURED"" field
3. EFFECTIVE DATE - Look for policy effective date (may be near policy info)
4. INCIDENT DATE/TIME - Look for ""DATE OF LOSS AND TIME"" field
5. INCIDENT LOCATION - Look for ""LOCATION OF LOSS"" section with STREET, CITY, STATE, ZIP
6. INCIDENT DESCRIPTION - Look for ""DESCRIPTION OF ACCIDENT"" field
7. CLAIMANT - Usually the insured/owner name
8. THIRD PARTIES - Look for ""OTHER VEHICLE / PROPERTY DAMAGED"" section
9. CONTACT DETAILS - Look for phone numbers and email addresses
10. ASSET TYPE - Look for ""YEAR MAKE MODEL"" under INSURED VEHICLE
11. ASSET ID - Look for ""V.I.N."" (Vehicle Identification Number)
12. ESTIMATED DAMAGE - Look for ""ESTIMATE AMOUNT"" field
13. CLAIM TYPE - Determine from accident description (Collision, Theft, etc.)

Return ONLY valid JSON in this EXACT format with NO additional text or markdown:

{{
    ""extractedFields"": {{
        ""PolicyNumber"": ""string or null"",
        ""PolicyholderName"": ""string or null"",
        ""EffectiveDate"": ""YYYY-MM-DD or null"",
        ""IncidentDate"": ""YYYY-MM-DD or null"",
        ""IncidentTime"": ""HH:MM or null"",
        ""IncidentLocation"": ""string or null"",
        ""IncidentDescription"": ""string or null"",
        ""Claimant"": ""string or null"",
        ""ThirdParties"": ""string or null"",
        ""ContactDetails"": ""string or null"",
        ""AssetType"": ""string (e.g., Car, Property, etc.) or null"",
        ""AssetId"": ""string (VIN, serial number, etc.) or null"",
        ""EstimatedDamage"": 0,
        ""ClaimType"": ""string (Collision, Theft, Injury, Property, etc.) or null"",
        ""Attachments"": ""string or null"",
        ""InitialEstimate"": ""string or null""
    }}
}}

DOCUMENT TEXT:
{text}

SPECIAL INSTRUCTIONS FOR ACORD FORMS:
- The policy number is typically a 6-10 digit alphanumeric code next to ""POLICY NUMBER""
- Dates are often in MM/DD/YYYY format - convert to YYYY-MM-DD
- Look for VIN numbers (17 characters containing letters and numbers)
- Damage estimates are usually dollar amounts
- If you see ""ACORD 2 (2016/10)"" - this is a standard form

IMPORTANT RULES:
1. If a field is not found, use null
2. Extract numbers as actual numbers (not strings)
3. Convert dates to YYYY-MM-DD format
4. Return ONLY the JSON object - no explanations, no markdown formatting
";

        var body = new
        {
            contents = new[]
      {
        new
        {
            parts = new[]
            {
                new { text = prompt }
            }
        }
    },
            generationConfig = new
            {
                temperature = 0.1,
                maxOutputTokens = 4096,  // ← Increase from 2048 to 4096
                topP = 0.95
            }
        };

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        int maxRetries = 3;
        int delaySeconds = 15;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var response = await client.PostAsync(
                url,
                new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
            );

            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                dynamic obj = JsonConvert.DeserializeObject(responseString);

                if (obj?.candidates == null || obj.candidates.Count == 0)
                {
                    throw new Exception("No response candidates from API");
                }

                string output = obj.candidates[0].content.parts[0].text;
                output = output.Replace("```json", "").Replace("```", "").Trim();

                return output;
            }

            if ((int)response.StatusCode == 429 && attempt < maxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                delaySeconds *= 2;
                continue;
            }

            throw new Exception($"API Error ({(int)response.StatusCode}): {responseString}");
        }

        throw new Exception("Max retries exceeded due to rate limiting");
    }
}