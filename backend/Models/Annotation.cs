using System.Drawing;
using static PDFBackend.Models.Constants;

namespace PDFBackend.Models
{
    public class Annotation
    {
        public AnnotationType Type;
        public int Page;
        public Point Position;
    }
}
