using InsuranceClaimsAI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public class ClaimController : Controller
{
    private readonly PdfService _pdfService;
    private readonly AiExtractionService _aiService;
    private readonly ILogger<ClaimController> _logger;

    public ClaimController(PdfService pdfService, AiExtractionService aiService, ILogger<ClaimController> logger)
    {
        _pdfService = pdfService;
        _aiService = aiService;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        // Clear any previous errors
        ViewBag.Error = null;
        ModelState.Clear();

        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("file", "Please select a file");
            return View("Index");
        }

        var fileExtension = Path.GetExtension(file.FileName).ToLower();

        // Allow both PDF and TXT files
        if (fileExtension != ".pdf" && fileExtension != ".txt")
        {
            ModelState.AddModelError("file", "Only PDF and TXT files are allowed");
            return View("Index");
        }

        if (file.Length > 10 * 1024 * 1024)
        {
            ModelState.AddModelError("file", "File size cannot exceed 10MB");
            return View("Index");
        }

        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
        var path = Path.Combine(uploadsFolder, uniqueFileName);

        try
        {
            // Save file
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream);
            }

            _logger.LogInformation($"File saved: {path}");

            // Extract text based on file type
            string text;
            if (fileExtension == ".pdf")
            {
                text = _pdfService.ExtractText(path);
                _logger.LogInformation($"Extracted {text?.Length ?? 0} characters from PDF");

                // Debug: Save extracted text to see what's being extracted
                var debugPath = Path.Combine(uploadsFolder, $"debug_{uniqueFileName}.txt");
                await System.IO.File.WriteAllTextAsync(debugPath, text);
                _logger.LogInformation($"Debug text saved to: {debugPath}");
            }
            else
            {
                text = System.IO.File.ReadAllText(path);
                _logger.LogInformation($"Read {text.Length} characters from TXT file");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new Exception("No text could be extracted from the file. The PDF might be scanned or empty.");
            }

            // Show first 500 characters for debugging
            var preview = text.Length > 500 ? text.Substring(0, 500) : text;
            _logger.LogInformation($"Text preview: {preview}");

            // Extract data using AI
            var aiJson = await _aiService.ExtractAsync(text);
            _logger.LogInformation($"AI Response: {aiJson}");

            if (string.IsNullOrWhiteSpace(aiJson))
            {
                throw new Exception("AI returned empty response");
            }

            var result = JsonConvert.DeserializeObject<ClaimResult>(aiJson);

            if (result == null)
            {
                throw new Exception("Failed to deserialize AI response - result is null");
            }

            if (result.extractedFields == null)
            {
                result.extractedFields = new ExtractedFields();
                _logger.LogWarning("extractedFields was null, created new instance");
            }

            // Apply routing rules
            ApplyRoutingRules(result);

            return View("Result", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing claim"); 


            // Check for 503 error and show friendly message
            if (ex.Message.Contains("503") || ex.Message.Contains("high demand"))
            {
                ViewBag.Error = "The AI service is currently experiencing high demand. Please try again in a few minutes.";
                ViewBag.ErrorDetails = "The Gemini API is temporarily unavailable. This is a temporary issue from Google's side.";
            }
            else if (ex.Message.Contains("429"))
            {
                ViewBag.Error = "API rate limit exceeded. Please wait a moment before trying again.";
                ViewBag.ErrorDetails = "You've reached the free tier limit. Try again in a few minutes.";
            }
            else
            {
                ViewBag.Error = ex.Message;
                ViewBag.ErrorDetails = ex.InnerException?.Message;
            }

            return View("Index");
        }
        finally
        {
            // Clean up temp file
            if (System.IO.File.Exists(path))
            {
                try
                {
                    System.IO.File.Delete(path);
                    _logger.LogInformation($"Deleted temp file: {path}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp file");
                }
            }
        }
    }

    private void ApplyRoutingRules(ClaimResult result)
    {
        result.missingFields = new List<string>();
        var fields = result.extractedFields;

        if (fields == null)
        {
            result.recommendedRoute = "Manual Review";
            result.reasoning = "No fields were extracted from the document";
            result.missingFields.Add("All fields");
            return;
        }

        // Define mandatory fields
        var mandatoryFields = new Dictionary<string, string?>
        {
            { "PolicyNumber", fields.PolicyNumber },
            { "PolicyholderName", fields.PolicyholderName },
            { "EffectiveDate", fields.EffectiveDate },
            { "IncidentDate", fields.IncidentDate },
            { "IncidentDescription", fields.IncidentDescription },
            { "Claimant", fields.Claimant },
            { "AssetType", fields.AssetType },
            { "ClaimType", fields.ClaimType }
        };

        // Check for missing mandatory fields
        foreach (var field in mandatoryFields)
        {
            if (string.IsNullOrWhiteSpace(field.Value))
            {
                result.missingFields.Add(field.Key);
            }
        }

        // PRIORITY 1: Check for Injury (highest priority)
        if (HasInjury(result.extractedFields))
        {
            result.recommendedRoute = "Specialist Queue";
            result.reasoning = "Injury detected - requires specialist review";
            return;
        }

        // PRIORITY 2: Check for Fraud
        if (HasFraudIndicators(result.extractedFields))
        {
            result.recommendedRoute = "Investigation Flag";
            result.reasoning = "Fraud indicators detected in incident description";
            return;
        }

        // Rule 3: Check for missing mandatory fields
        if (result.missingFields.Any())
        {
            result.recommendedRoute = "Manual Review";
            result.reasoning = $"Missing mandatory fields: {string.Join(", ", result.missingFields)}";
            return;
        }

        // Rule 4: Check estimated damage for fast track
        if (fields.EstimatedDamage > 0 && fields.EstimatedDamage < 25000)
        {
            result.recommendedRoute = "Fast Track";
            result.reasoning = $"Estimated damage (${fields.EstimatedDamage:N0}) qualifies for fast track processing";
            return;
        }

        // Default routing
        result.recommendedRoute = "Standard Review";
        result.reasoning = "Standard processing required";
    }

    private bool HasInjury(ExtractedFields fields)
    {
        if (fields == null) return false;

        // Check Claim Type
        if (!string.IsNullOrEmpty(fields.ClaimType) &&
            fields.ClaimType.ToLower().Contains("injury"))
            return true;

        // Check Description
        var injuryKeywords = new[] {
            "injury", "injuries", "injured", "medical", "hospital", "ambulance",
            "whiplash", "back pain", "headache", "fracture", "bleeding",
            "pain", "doctor", "treatment", "surgery", "hurt"
        };

        if (!string.IsNullOrEmpty(fields.IncidentDescription))
        {
            var description = fields.IncidentDescription.ToLower();
            if (injuryKeywords.Any(k => description.Contains(k)))
                return true;
        }

        return false;
    }

    private bool HasFraudIndicators(ExtractedFields fields)
    {
        if (fields == null) return false;

        var fraudKeywords = new[] { "fraud", "inconsistent", "staged", "suspicious", "fake" };

        if (!string.IsNullOrEmpty(fields.IncidentDescription))
        {
            var description = fields.IncidentDescription.ToLower();
            if (fraudKeywords.Any(k => description.Contains(k)))
                return true;
        }

        return false;
    }
}