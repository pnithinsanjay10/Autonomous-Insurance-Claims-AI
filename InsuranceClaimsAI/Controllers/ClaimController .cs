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
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("file", "Please select a file");
            return View("Index");
        }

        var fileExtension = Path.GetExtension(file.FileName).ToLower();
        if (fileExtension != ".pdf")
        {
            ModelState.AddModelError("file", "Only PDF files are allowed");
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
            // Save and extract PDF
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream);
            }

            var text = _pdfService.ExtractText(path);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new Exception("No text could be extracted from the PDF");
            }

            _logger.LogInformation($"Extracted {text.Length} characters from PDF");

            // Extract data using AI
            var aiJson = await _aiService.ExtractAsync(text);
            var result = JsonConvert.DeserializeObject<ClaimResult>(aiJson);

            if (result?.extractedFields == null)
            {
                throw new Exception("Failed to deserialize AI response");
            }

            // Apply routing rules
            ApplyRoutingRules(result);

            return View("Result", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing claim");
            ViewBag.Error = ex.Message;
            return View("Index");
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                try { System.IO.File.Delete(path); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp file"); }
            }
        }
    }
    private void ApplyRoutingRules(ClaimResult result)
    {
        result.missingFields = new List<string>();
        var fields = result.extractedFields;

        // Define mandatory fields
        var mandatoryFields = new Dictionary<string, string>
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