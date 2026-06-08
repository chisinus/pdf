using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PDFBackend.Models
{
    public class AnnotationPosition
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }
    }

    // Using System.Text.Json polymorphism to automatically deserialize 
    // the correct derived class based on the "type" property from the frontend.
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(AnnotationRectangle), "rectangle")]
    [JsonDerivedType(typeof(AnnotationFreehand), "freehand")]
    [JsonDerivedType(typeof(AnnotationText), "text")]
    [JsonDerivedType(typeof(AnnotationHighlight), "highlight")]
    [JsonDerivedType(typeof(AnnotationArrow), "arrow")] // Added in case you implement Arrow
    public abstract class AnnotationBase
    {
        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("position")]
        public AnnotationPosition Position { get; set; }
    }

    public class AnnotationRectangle : AnnotationBase
    {
        [JsonPropertyName("width")]
        public double Width { get; set; }

        [JsonPropertyName("height")]
        public double Height { get; set; }

        [JsonPropertyName("color")]
        public string Color { get; set; }
    }

    public class AnnotationFreehand : AnnotationBase
    {
        [JsonPropertyName("points")]
        public List<AnnotationPosition> Points { get; set; }

        [JsonPropertyName("color")]
        public string Color { get; set; }

        [JsonPropertyName("strokeWidth")]
        public double StrokeWidth { get; set; }
    }

    public class AnnotationText : AnnotationBase
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("color")]
        public string Color { get; set; }

        [JsonPropertyName("fontSize")]
        public double FontSize { get; set; }
    }

    public class AnnotationHighlight : AnnotationBase
    {
        [JsonPropertyName("color")]
        public string Color { get; set; }

        [JsonPropertyName("opacity")]
        public double Opacity { get; set; }
    }
    
    public class AnnotationArrow : AnnotationBase
    {
        [JsonPropertyName("endPosition")]
        public AnnotationPosition EndPosition { get; set; }

        [JsonPropertyName("color")]
        public string Color { get; set; }

        [JsonPropertyName("strokeWidth")]
        public double StrokeWidth { get; set; }
    }
}