namespace PDFBackend.Models
{
    public class PageMetadata
    {
        public int PageNumber;
        public int Width;
        public int Height;
        public int Rotation;
        public AnnotationBase[]? Annotations;
    }
}
