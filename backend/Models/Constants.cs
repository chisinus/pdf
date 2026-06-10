using System.ComponentModel;

namespace PDFBackend.Models
{
    public class Constants
    {
        public enum AnnotationType
        {
            [Description("Arrow")]
            Arrow,
            [Description("Rectangle")]
            Rectangle,
            [Description("Circle")]
            Circle,
            [Description("Text")]
            Text,
            [Description("Highlight")]
            Highlight,
            [Description("Freehand")]
            Freehand,
        }
    }
}
