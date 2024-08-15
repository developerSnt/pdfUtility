

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using PdfSharp.Pdf.IO;
using System.Reflection.Metadata;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/merge-pdfs", async (PdfMergeRequest request) =>
{
    if (request.PdfUrls == null || !request.PdfUrls.Any())
    {
        return Results.BadRequest("No PDF URLs provided.");
    }

    string mergedFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MergedPdfs");

    try
    {
        Directory.CreateDirectory(mergedFolderPath);

        string outputFilePath = Path.Combine(mergedFolderPath, $"merged_{DateTime.Now:yyyyMMddHHmmss}.pdf");

        using (var outputDocument = new PdfSharp.Pdf.PdfDocument())
        {
            using (var httpClient = new HttpClient())
            {
                foreach (var pdfUrl in request.PdfUrls)
                {
                    try
                    {
                        var response = await httpClient.GetAsync(pdfUrl);
                        response.EnsureSuccessStatusCode();

                        using (var inputStream = await response.Content.ReadAsStreamAsync())
                        {
                            using (var inputDocument = PdfSharp.Pdf.IO.PdfReader.Open(inputStream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                            {
                                for (int idx = 0; idx < inputDocument.PageCount; idx++)
                                {
                                    var page = inputDocument.Pages[idx];
                                    outputDocument.AddPage(page);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest($"Error processing file {pdfUrl}: {ex.Message}");
                    }
                }
            }

            outputDocument.Save(outputFilePath);
        }

        var fileBytes = await File.ReadAllBytesAsync(outputFilePath);
        var fileName = Path.GetFileName(outputFilePath);
        return Results.File(fileBytes, "application/pdf", fileName);
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Error merging PDFs: {ex.Message}");
    }
});

app.MapPost("/hide-text", async (SearchRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.PdfUrl) || string.IsNullOrWhiteSpace(request.SearchText))
    {
        return Results.BadRequest("Invalid request. 'PdfUrl' and 'SearchText' must be provided.");
    }

    Stream pdfStream;
    try
    {
        if (Uri.TryCreate(request.PdfUrl, UriKind.Absolute, out Uri uriResult) &&
            (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(request.PdfUrl);
            response.EnsureSuccessStatusCode();
            pdfStream = await response.Content.ReadAsStreamAsync();
        }
        else if (Uri.TryCreate(request.PdfUrl, UriKind.Absolute, out Uri fileUri) &&
                 fileUri.Scheme == Uri.UriSchemeFile)
        {
            var filePath = new Uri(request.PdfUrl).LocalPath;
            pdfStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        else
        {
            return Results.BadRequest("Invalid URL scheme. Only HTTP/HTTPS and file schemes are supported.");
        }

        // Load the PDF with PdfPig to get text positions
        var textPositions = new List<TextPosition>();
        using (var pdfDocument = UglyToad.PdfPig.PdfDocument.Open(pdfStream))
        {
            foreach (var page in pdfDocument.GetPages())
            {
                textPositions.AddRange(ExtractTextPositions(page, request.SearchText));
            }
        }
        double rectangleWidth = 200;
        double rectangleHeight = 50;
        var outputPdfStream = new MemoryStream();
        using (var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Modify))
        {
            foreach (var page in pdfDocument.Pages)
            {
                var gfx = XGraphics.FromPdfPage(page);

                // Apply redactions
                foreach (var textPosition in textPositions)
                {
                    var rect = new XRect(
                        textPosition.X,
                        page.Height - textPosition.Y,
                        textPosition.Width,
                        textPosition.Height
                       );
                    gfx.DrawRectangle(XBrushes.White, rect); // Draw a pink rectangle to cover the text
                }
            }

            // Save the output PDF document to the MemoryStream
            pdfDocument.Save(outputPdfStream);
        }

        // Reset position for reading
        outputPdfStream.Position = 0;

        return Results.File(outputPdfStream, "application/pdf", "UpdatedPdf.pdf");
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Error processing PDF: {ex.Message}");
    }
});
// Extract text positions for redaction
static IEnumerable<TextPosition> ExtractTextPositions(Page page, string searchText)
{
    var positions = new List<TextPosition>();

    foreach (var word in page.GetWords())
    {
        if (word.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase))
        {
            positions.Add(new TextPosition
            {
                X = word.BoundingBox.Left,
                Y = word.BoundingBox.Top,
                Width = word.BoundingBox.Width,
                Height = word.BoundingBox.Height
            });
        }
    }

    return positions;
}

// Define models


app.Run();
public class SearchRequest
{
    public string PdfUrl { get; set; }
    public string SearchText { get; set; }
}

public class PageDetail
{
    public int PageIndex { get; set; }
    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
    public IEnumerable<TextPosition> TextPositions { get; set; }
}

public class TextPosition
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
}

public class CreateRectanglesRequest
{
    public string PdfUrl { get; set; }
    public List<PageDetail> PageDetails { get; set; }
}

public class PdfMergeRequest
{
    public List<string> PdfUrls { get; set; }
}