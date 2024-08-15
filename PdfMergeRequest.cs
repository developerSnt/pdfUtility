namespace pdfMarg
{
    public class PdfMergeRequest
    {
        public List<string> PdfUrls { get; set; }
    }



    public class TextPosition
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }


}
