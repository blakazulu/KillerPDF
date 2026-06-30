using System;

namespace Scalpel.Services
{
    /// <summary>
    /// App-only half of <see cref="TransformService"/>: wires the native Docnet/PDFium rasterizer
    /// into the WPF-free core. This file is excluded from the unit-test project (which links only
    /// <c>TransformService.cs</c> and never rasterizes), keeping pdfium out of the test run.
    /// </summary>
    public static partial class TransformService
    {
        static TransformService()
        {
            RasterizerFactory = path => new DocnetPageRasterizer(path);
        }
    }
}
