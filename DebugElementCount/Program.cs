using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

var docxPath = @"E:\main-service-dotnet\Tests\TestData\bab2.docx";
if (!File.Exists(docxPath)) { Console.WriteLine("File not found"); return; }

using var doc = WordprocessingDocument.Open(docxPath, false);
var body = doc.MainDocumentPart!.Document.Body!;

Console.WriteLine("Checking element identity consistency...");

var firstRunList = body.Elements().ToList();
var secondRunList = body.Elements().ToList();

if (firstRunList.Count != secondRunList.Count)
{
    Console.WriteLine($"Count mismatch! {firstRunList.Count} vs {secondRunList.Count}");
    return;
}

int mismatchCount = 0;
for (int i = 0; i < firstRunList.Count; i++)
{
    if (!object.ReferenceEquals(firstRunList[i], secondRunList[i]))
    {
        mismatchCount++;
    }
}

Console.WriteLine($"Total elements: {firstRunList.Count}");
Console.WriteLine($"Identity mismatches: {mismatchCount}");

if (mismatchCount > 0)
{
    Console.WriteLine("FAIL: calling body.Elements() twice returns different objects!");
}
else
{
    Console.WriteLine("PASS: objects preserve identity.");
}
