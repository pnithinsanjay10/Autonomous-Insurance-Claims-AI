using iText.Kernel.Pdf;
using iText.Forms;
using iText.Forms.Fields;
using System.Text;
using System.Reflection.PortableExecutable;

public class PdfService
{
    public string ExtractText(string filePath)
    {
        var sb = new StringBuilder();

        using (var pdfDoc = new PdfDocument(new PdfReader(filePath)))
        {
            // Get the AcroForm (fillable form) from the PDF
            var form = PdfAcroForm.GetAcroForm(pdfDoc, false);

            if (form != null)
            {
                // Get all form fields
                var fields = form.GetAllFormFields();

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

        return sb.ToString();
    }
}