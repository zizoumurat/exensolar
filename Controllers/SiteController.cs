using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;

namespace ExenSolar.Umbraco.Controllers;

public sealed class SiteController(
    IPublishedContentQuery contentQuery,
    IContentService contentService) : Controller
{
    private static readonly IReadOnlyDictionary<string, string> Pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["hakkimizda"] = "Hakkımızda",
        ["hizmetlerimiz"] = "Hizmetlerimiz",
        ["referanslar"] = "Referanslar",
        ["medya"] = "Medya",
        ["iletisim"] = "İletişim"
    };

    public IActionResult Index(string? slug)
    {
        var roots = contentQuery.ContentAtRoot();
        var home = roots.FirstOrDefault(x => x.ContentType.Alias == "exenPage" && x.Name == "Ana Sayfa");
        if (home is null) return NotFound();

        var route = (slug ?? string.Empty).Trim('/');
        if (route.Length == 0) return View("~/views/exenPage.cshtml", home);
        if (!Pages.TryGetValue(route, out var pageName)) return NotFound();

        var pageId = contentService.GetRootContent()
            .FirstOrDefault(x => x.Name == pageName)?.Id;
        var page = pageId.HasValue ? contentQuery.Content(pageId.Value) : null;
        return page is null ? NotFound() : View("~/views/exenPage.cshtml", page);
    }
}
