namespace PDFBackend.Models
{
    public class PageMetadata
    {
        public int PageNumber;
        public double Width;
        public double Height;
        public int Rotation;
        public AnnotationBase[]? Annotations;
    }
}
