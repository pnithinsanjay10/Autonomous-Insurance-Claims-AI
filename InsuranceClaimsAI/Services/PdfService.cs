using iText.Kernel.Pdf;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf.Canvas.Parser;
using System.Text;
using System;
using System.Collections.Generic;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

public class PdfService
{
    public string ExtractText(string filePath)
    {
        var sb = new StringBuilder();

        try
        {
            using (var pdfDoc = new PdfDocument(new PdfReader(filePath)))
            {
                // Get the AcroForm
                var form = PdfAcroForm.GetAcroForm(pdfDoc, false);

                if (form != null)
                {
                    // Use GetAllFormFields() instead of GetFormFields()
                    var fields = form.GetAllFormFields();

                    if (fields != null && fields.Count > 0)
                    {
                        foreach (var field in fields)
                        {
                            var fieldName = field.Key;
                            var fieldValue = field.Value.GetValueAsString();

                            if (!string.IsNullOrEmpty(fieldValue))
                            {
                                sb.AppendLine($"{fieldName}: {fieldValue}");
                            }
                        }
                    }
                }

                // Also extract page text for non-form PDFs
                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                {
                    var page = pdfDoc.GetPage(i);
                    var strategy = new SimpleTextExtractionStrategy();
                    var text = PdfTextExtractor.GetTextFromPage(page, strategy);
                    if (!string.IsNullOrEmpty(text))
                    {
                        sb.AppendLine(text);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"PDF extraction failed: {ex.Message}", ex);
        }

        return sb.ToString();
    }
}