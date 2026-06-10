using System.Collections.Generic;
using System.Threading.Tasks;
using PDFBackend.Models;

namespace PDFBackend.Services;

public interface IPdfAnnotationService
{
    Task<bool> SaveAnnotationsAsync(string id, List<AnnotationBase> annotations);
    Task<bool> ApplyAnnotationsAsync(string id);
}