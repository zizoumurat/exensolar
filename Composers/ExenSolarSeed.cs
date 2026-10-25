using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using System.Text.Json;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;

namespace ExenSolar.Umbraco.Composers;

public sealed class ExenSolarSeed(
    IContentTypeService contentTypes,
    IContentService contents,
    ITemplateService templates,
    IDataTypeService dataTypes,
    PropertyEditorCollection propertyEditors,
    IConfigurationEditorJsonSerializer configurationSerializer,
    IShortStringHelper shortStrings,
    ILogger<ExenSolarSeed> logger) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string PageAlias = "exenPage";
    private const string ItemAlias = "exenItem";
    private const string AboutAlias = "exenAboutPage";
    private const string SettingsAlias = "exenSiteSettings";
    private const string SliderFolderAlias = "exenSliderFolder";
    private const string HomeSectionsAlias = "homeSections";

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var userKey = Constants.Security.SuperUserKey;
            var homeSectionsDataType = await dataTypes.GetAsync("Exen Solar Ana Sayfa Bölümleri");
            if (homeSectionsDataType is null)
            {
                var textAreaEditor = propertyEditors[Constants.PropertyEditors.Aliases.TextArea];
                if (textAreaEditor is null) throw new InvalidOperationException("Umbraco metin alanı editörü bulunamadı.");
                var dataType = new DataType(textAreaEditor, configurationSerializer, -1)
                {
                    Name = "Exen Solar Ana Sayfa Bölümleri",
                    EditorUiAlias = "ExenSolar.PropertyEditorUi.HomeSections"
                };
                var dataTypeResult = await dataTypes.CreateAsync(dataType, userKey);
                if (!dataTypeResult.Success) throw new InvalidOperationException("Ana sayfa yönetim alanı veri tipi oluşturulamadı.");
                homeSectionsDataType = dataType;
            }
            var existingPage = contentTypes.Get(PageAlias);
            var existingItem = contentTypes.Get(ItemAlias);
            var template = await templates.GetAsync("ExenPage");
            if (template is null)
            {
                await templates.CreateAsync("Exen Solar Sayfa", "ExenPage", string.Empty, userKey, null);
                template = await templates.GetAsync("ExenPage");
            }

            if (existingPage is null)
            {
                var page = new ContentType(shortStrings, -1)
                {
                    Alias = PageAlias, Name = "Exen Solar Sayfa", Icon = "icon-sun color-orange",
                    AllowedAsRoot = true, IsElement = false,
                    AllowedContentTypes = Array.Empty<ContentTypeSort>()
                };
                AddGroup(page, "İçerik", new[]
                {
                    Text("Sayfa başlığı", "pageTitle"), Text("Üst başlık", "eyebrow"),
                    LongText("Giriş metni", "intro"), LongText("Ana içerik", "bodyText"),
                    Text("Görsel yolu (eski)", "imageUrl"), Media("Görsel", "primaryImage"), Media("İkinci görsel", "secondaryImage"), Text("Buton metni", "buttonText"),
                    Text("Buton bağlantısı", "buttonUrl")
                });
                AddGroup(page, "Site ayarları (ana sayfada)", new[]
                {
                    Text("Telefon", "phone"), Text("E-posta", "email"), LongText("Adres", "address"),
                    Text("Logo yolu (eski)", "logoUrl"), Media("Logo", "logoMedia"), Text("Alt bilgi logo yolu (eski)", "footerLogoUrl"), Media("Alt bilgi logosu", "footerLogoMedia")
                });
                if (template is not null) { page.AllowedTemplates = new[] { template }; page.SetDefaultTemplate(template); }
                await contentTypes.CreateAsync(page, userKey);
            }

            var aboutType = contentTypes.Get(AboutAlias);
            if (aboutType is null)
            {
                aboutType = new ContentType(shortStrings, -1) { Alias = AboutAlias, Name = "Exen Solar Hakkımızda", Icon = "icon-info color-orange", AllowedAsRoot = false };
                AddGroup((ContentType)aboutType, "İçerik", new[] { Text("Sayfa başlığı", "pageTitle"), LongText("Giriş metni", "intro"), LongText("Ana içerik", "bodyText"), Media("Görsel", "primaryImage"), Media("İkinci görsel", "secondaryImage") });
                if (template is not null) { aboutType.AllowedTemplates = new[] { template }; aboutType.SetDefaultTemplate(template); }
                await contentTypes.CreateAsync(aboutType, userKey);
            }

            var settingsType = contentTypes.Get(SettingsAlias);
            if (settingsType is null)
            {
                settingsType = new ContentType(shortStrings, -1) { Alias = SettingsAlias, Name = "Exen Solar Site Ayarları", Icon = "icon-settings color-orange", AllowedAsRoot = false };
                AddGroup((ContentType)settingsType, "Site ayarları", new[] { Text("Telefon", "phone"), Text("E-posta", "email"), LongText("Adres", "address"), Media("Logo", "logoMedia"), Media("Alt bilgi logosu", "footerLogoMedia") });
                await contentTypes.CreateAsync(settingsType, userKey);
            }
            var sliderFolderType = contentTypes.Get(SliderFolderAlias);
            if (sliderFolderType is null)
            {
                sliderFolderType = new ContentType(shortStrings, -1) { Alias = SliderFolderAlias, Name = "Kayan Resimler", Icon = "icon-slideshow color-orange", AllowedAsRoot = true };
                await contentTypes.CreateAsync(sliderFolderType, userKey);
            }

            // Add media picker fields to projects created before the media migration.
            existingPage = contentTypes.Get(PageAlias);
            if (existingPage is not null)
            {
                var changed = false;
                foreach (var alias in new[] { "imageUrl", "buttonText", "buttonUrl", "primaryImage", "secondaryImage", "phone", "email", "address", "logoUrl", "footerLogoUrl", "logoMedia", "footerLogoMedia" })
                    if (existingPage.PropertyTypes.Any(x => x.Alias == alias)) { existingPage.RemovePropertyType(alias); changed = true; }
                if (existingPage.PropertyTypes.Any(x => x.Alias == HomeSectionsAlias)) { existingPage.RemovePropertyType(HomeSectionsAlias); changed = true; }
                existingPage.RemovePropertyGroup("site-ayarları-(ana-sayfada)");
                existingPage.RemovePropertyGroup("ana-sayfa-bölümleri");
                if (!existingPage.PropertyTypes.Any(x => x.Alias == HomeSectionsAlias))
                {
                    AddGroup((ContentType)existingPage, "Ana Sayfa Bölümleri", new[] { new PropertyType(shortStrings, homeSectionsDataType) { Name = "Ana sayfa bölümleri", Alias = HomeSectionsAlias } });
                    changed = true;
                }
                if (changed) await contentTypes.CreateAsync(existingPage, userKey);
            }

            if (existingItem is null)
            {
                var item = new ContentType(shortStrings, -1)
                {
                    Alias = ItemAlias, Name = "Slider / Hizmet / Referans / Medya Kaydı",
                    Icon = "icon-thumbnails-small color-green", AllowedAsRoot = false, IsElement = false
                };
                AddGroup(item, "Kayıt", new[]
                {
                    Text("Kayıt türü (slider, hizmet, referans, medya)", "itemType"),
                    Text("Başlık", "itemTitle"), LongText("Açıklama", "itemText"),
                    Text("Görsel yolu", "itemImage"), Text("Bağlantı", "itemLink"),
                    Text("Bağlantı metni", "itemLinkText")
                });
                await contentTypes.CreateAsync(item, userKey);
            }
            var sliderFolderDefinition = contentTypes.Get(SliderFolderAlias);
            var itemDefinition = contentTypes.Get(ItemAlias);
            if (sliderFolderDefinition is not null && itemDefinition is not null && !sliderFolderDefinition.AllowedContentTypes.Any(x => x.Key == itemDefinition.Key))
            {
                sliderFolderDefinition.AllowedContentTypes = new[] { new ContentTypeSort(itemDefinition.Key, itemDefinition.Id, itemDefinition.Alias) };
                await contentTypes.CreateAsync(sliderFolderDefinition, userKey);
            }

            var persistedPageType = contentTypes.Get(PageAlias);
            if (persistedPageType is not null && (!persistedPageType.AllowedTemplates.Any() || persistedPageType.AllowedContentTypes?.Any() == true))
            {
                persistedPageType.AllowedContentTypes = Array.Empty<ContentTypeSort>();
                if (!persistedPageType.AllowedTemplates.Any())
                    await contentTypes.CreateTemplateAsync(persistedPageType.Key, "Exen Solar Sayfa", PageAlias, true, userKey);
                await contentTypes.UpdateAsync(persistedPageType, userKey);
            }

            var existingHome = contents.GetRootContent().FirstOrDefault(x => x.ContentType.Alias == PageAlias && x.Name == "Ana Sayfa");
            if (existingHome is not null)
            {
                var sliderFolder = contents.GetRootContent().FirstOrDefault(x => x.ContentType.Alias == SliderFolderAlias);
                if (sliderFolder is null)
                {
                    sliderFolder = contents.Create("Kayan Resimler", -1, SliderFolderAlias);
                    contents.SaveAndPublish(sliderFolder, Array.Empty<string>(), Constants.Security.SuperUserId);
                }
                foreach (var slide in contents.GetPagedChildren(existingHome.Id, 0, 100, out _).Where(x => x.ContentType.Alias == ItemAlias && x.GetValue<string>("itemType") == "slider"))
                    contents.Move(slide, sliderFolder.Id, Constants.Security.SuperUserId);
                foreach (var child in contents.GetPagedChildren(existingHome.Id, 0, 100, out _).Where(x => x.ContentType.Alias == PageAlias && x.Name != "Site Ayarları"))
                    contents.Move(child, -1, Constants.Security.SuperUserId);
                var aboutNode = contents.GetPagedChildren(existingHome.Id, 0, 20, out _).FirstOrDefault(x => x.Name == "Hakkımızda");
                var persistedAboutType = contentTypes.Get(AboutAlias);
                if (aboutNode is not null && persistedAboutType is not null && aboutNode.ContentType.Alias != AboutAlias)
                {
                    ((dynamic)aboutNode).ChangeContentType(persistedAboutType, true);
                    contents.SaveAndPublish(aboutNode, Array.Empty<string>(), Constants.Security.SuperUserId);
                }
                var settingsNode = contents.GetPagedChildren(existingHome.Id, 0, 50, out _).FirstOrDefault(x => x.ContentType.Alias == SettingsAlias);
                if (settingsNode is null)
                {
                    settingsNode = contents.Create("Site Ayarları", existingHome.Id, SettingsAlias);
                    Set(settingsNode, "phone", existingHome.GetValue<string>("phone") ?? "+90 342 345 67 89");
                    Set(settingsNode, "email", existingHome.GetValue<string>("email") ?? "info@exensolar.tr");
                    Set(settingsNode, "address", existingHome.GetValue<string>("address") ?? "");
                    contents.SaveAndPublish(settingsNode, Array.Empty<string>(), Constants.Security.SuperUserId);
                }
                EnsureHomeIntroduction(existingHome);
                EnsureHomeSlider(existingHome);
                EnsureHomePartners(existingHome);
                MigrateHomeSections(existingHome, sliderFolder);
                return;
            }

            var home = contents.Create("Ana Sayfa", -1, PageAlias);
            Set(home, "pageTitle", "Exen SLR Enerji Sistemleri");
            Set(home, "eyebrow", "Exen Solar");
            Set(home, "bodyText", HomeIntroductionText);
            Set(home, "phone", "+90 342 345 67 89");
            Set(home, "email", "info@exensolar.tr");
            Set(home, "address", "Mücahitler Mah 52083 nolu Sk. No:42 Yasem İş Merkezi Kat:7 Daire:709 Şehitkamil/Gaziantep");
            Set(home, "logoUrl", "/assets/img/exen_logo.png");
            Set(home, "footerLogoUrl", "/assets/img/exen_logo_2.png");
            contents.SaveAndPublish(home, Array.Empty<string>(), Constants.Security.SuperUserId);

            var pages = new[]
            {
                ("Hakkımızda", "hakkimizda", "Gelecek için yenilenebilir enerji projeleri geliştiriyoruz."),
                ("Hizmetlerimiz", "hizmetlerimiz", "Enerji çözümlerimizi keşfedin."),
                ("Referanslar", "referanslar", "Gelecek için yenilenebilir enerji projeleri geliştiriyoruz."),
                ("Medya", "medya", "Medya merkezimizde Exen Solar hakkında haberler ve görseller bulabilirsiniz."),
                ("İletişim", "iletisim", "Bizimle iletişime geçin.")
            };
            foreach (var (name, urlName, intro) in pages)
            {
                var node = contents.Create(name, home.Id, PageAlias);
                node.SetValue("pageTitle", name);
                node.SetValue("intro", intro);
                contents.SaveAndPublish(node, Array.Empty<string>(), Constants.Security.SuperUserId);
            }

            var services = contents.GetPagedChildren(home.Id, 0, 20, out _)
                .First(x => x.Name == "Hizmetlerimiz");
            var serviceData = new[]
            {
                ("Güneş Enerji Santralleri Projelendirme ve Danışmanlık Hizmetleri", "/assets/img/recent-projects-1.jpg"),
                ("Güneş Enerji Santralleri Kurulum ve Montaj Hizmetleri", "/assets/img/recent-projects-2.jpg"),
                ("Güneş Enerji Santralleri Denetim ve Kabul İşlemleri", "/assets/img/recent-projects-4.jpg"),
                ("Güneş Enerji Santralleri İşletme ve Bakım Onarım Hizmetleri", "/assets/img/recent-projects-5.jpg"),
                ("Yüksek Gerilim Trafo Tesisleri Kurulum ve Bakım Onarım Hizmetleri", "/assets/img/recent-projects-6.jpg"),
                ("Enerji Nakil Hatları Projelendirme ve Kurulum Hizmetleri", "/assets/img/recent-projects-7.jpg")
            };
            foreach (var (title, image) in serviceData) CreateItem(services.Id, "hizmet", title, image);

            var references = contents.GetPagedChildren(home.Id, 0, 20, out _).First(x => x.Name == "Referanslar");
            for (var i = 1; i <= 4; i++) CreateItem(references.Id, "referans", "Referans", $"/assets/img/sponsor-{i}.jpg");

            var media = contents.GetPagedChildren(home.Id, 0, 20, out _).First(x => x.Name == "Medya");
            CreateItem(media.Id, "medya", "Exen Solar Medya", "/assets/img/video.jpg");
            CreateItem(media.Id, "medya", "Exen Solar Medya", "/assets/img/video-2.jpg");

            var initialSliderFolder = contents.Create("Kayan Resimler", -1, SliderFolderAlias);
            contents.SaveAndPublish(initialSliderFolder, Array.Empty<string>(), Constants.Security.SuperUserId);
            EnsureHomeSlider(home);
            EnsureHomePartners(home);
            MigrateHomeSections(home, initialSliderFolder);
            logger.LogInformation("Exen Solar content types and starter content were created.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exen Solar starter content could not be created. It will be retried at next startup.");
        }
    }

    private void CreateItem(int parentId, string type, string title, string image)
    {
        var item = contents.Create(title, parentId, ItemAlias);
        Set(item, "itemType", type); Set(item, "itemTitle", title); Set(item, "itemImage", image);
        contents.SaveAndPublish(item, Array.Empty<string>(), Constants.Security.SuperUserId);
    }

    private void EnsureHomeSlider(IContent home)
    {
        var sourceSlides = new[]
        {
            ("Enerjinin Akıllı Yol Haritası", "Güneş enerjisi, nakil hatları ve trafo merkezlerinde yenilikçi planlama ve kusursuz uygulama sunuyoruz.", "/assets/img/featured-area.jpg"),
            ("Enerjide Doğru Çözümün Adresi", "İşverenlerimize hem ekonomik hem sürdürülebilir, tüm aşamaları kontrol altında tutulan projeler sunuyoruz.", "/assets/img/featured-area-2.jpg"),
            ("Verimli Çözümler, Güçlü Sistemler", "Verimli Çözümler, Güçlü Sistemler", "/assets/img/featured-area-3.jpg"),
            ("Gücünü Güneşten Alan Gelecek", "Yenilenebilir enerji projelerinde mühendislik gücümüzle güvenilir ve uzun ömürlü çözümler üretiyoruz.", "/assets/img/featured-area-4.jpg")
        };

        var sliderParent = contents.GetRootContent().FirstOrDefault(x => x.ContentType.Alias == SliderFolderAlias) ?? home;
        var slides = contents.GetPagedChildren(sliderParent.Id, 0, 100, out _)
            .Where(x => x.ContentType.Alias == ItemAlias && x.GetValue<string>("itemType") == "slider")
            .ToList();

        // Replace the obsolete draft slide with the first slide from the source HTML.
        var legacyFirst = slides.FirstOrDefault(x =>
            x.Name == "Güvenilir Enerji Altyapılarının Mimarı" &&
            x.GetValue<string>("itemImage") == "/assets/img/slide-two-2.jpg");
        if (legacyFirst is not null)
        {
            Set(legacyFirst, "itemTitle", sourceSlides[0].Item1);
            Set(legacyFirst, "itemText", sourceSlides[0].Item2);
            Set(legacyFirst, "itemImage", sourceSlides[0].Item3);
            Set(legacyFirst, "itemLink", "/iletisim/");
            Set(legacyFirst, "itemLinkText", "Teklif Al");
            legacyFirst.Name = sourceSlides[0].Item1;
            contents.SaveAndPublish(legacyFirst, Array.Empty<string>(), Constants.Security.SuperUserId);
            slides.Remove(legacyFirst);
            slides.Add(legacyFirst);
        }

        foreach (var (title, text, image) in sourceSlides)
        {
            var slide = slides.FirstOrDefault(x => x.Name == title || x.GetValue<string>("itemTitle") == title);
            if (slide is null)
            {
                slide = contents.Create(title, sliderParent.Id, ItemAlias);
                Set(slide, "itemType", "slider");
            }

            var currentImage = slide.GetValue<string>("itemImage");
            if (string.IsNullOrWhiteSpace(currentImage) || currentImage.StartsWith("/assets/img/slide-two-", StringComparison.OrdinalIgnoreCase))
            {
                Set(slide, "itemTitle", title);
                Set(slide, "itemText", text);
                Set(slide, "itemImage", image);
                Set(slide, "itemLink", "/iletisim/");
                Set(slide, "itemLinkText", "Teklif Al");
                contents.SaveAndPublish(slide, Array.Empty<string>(), Constants.Security.SuperUserId);
            }
        }
    }

    private const string HomeIntroductionText = "Exen SLR Enerji Sistemleri, yenilenebilir enerjinin geleceğine yön veren, sahadaki tecrübesini mühendislik gücüyle birleştiren dinamik bir enerji şirketidir. 15 yılı aşkın proje yönetimi ve saha deneyimimizle, güneş enerji santralleri, enerji nakil hatları ve trafo merkezleri alanlarında yenilikçi, güvenilir ve sürdürülebilir çözümler sunuyoruz. Her projede; kalite, verimlilik ve iş güvenliğini merkezine alan yaklaşımımızla fark yaratıyoruz. Planlamadan devreye almaya, işletmeden bakım süreçlerine kadar tüm aşamalarda mükemmelliği hedefleyen bir iş anlayışıyla hareket ediyoruz.";

    private void EnsureHomeIntroduction(IContent home)
    {
        var changed = false;
        var currentTitle = home.GetValue<string>("pageTitle");
        if (string.IsNullOrWhiteSpace(currentTitle) || currentTitle == "Enerjide Doğru Çözümün Adresi")
        {
            Set(home, "pageTitle", "Exen SLR Enerji Sistemleri");
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(home.GetValue<string>("eyebrow")))
        {
            Set(home, "eyebrow", "Exen Solar");
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(home.GetValue<string>("bodyText")))
        {
            Set(home, "bodyText", HomeIntroductionText);
            changed = true;
        }

        if (changed)
        {
            contents.SaveAndPublish(home, Array.Empty<string>(), Constants.Security.SuperUserId);
        }
    }

    private void EnsureHomePartners(IContent home)
    {
        var partnerData = new[]
        {
            ("Huawei", "/assets/img/sponsor-1.jpg"),
            ("Fimer", "/assets/img/sponsor-2.jpg"),
            ("Sungrow", "/assets/img/sponsor-3.jpg"),
            ("Grup Arge", "/assets/img/sponsor-4.jpg")
        };
        var existingPartners = contents.GetPagedChildren(home.Id, 0, 100, out _)
            .Where(x => x.ContentType.Alias == ItemAlias && x.GetValue<string>("itemType") == "partner")
            .ToList();

        foreach (var (title, image) in partnerData)
        {
            if (existingPartners.Any(x => x.GetValue<string>("itemTitle") == title)) continue;
            CreateItem(home.Id, "partner", title, image);
        }
    }

    private void MigrateHomeSections(IContent home, IContent sliderFolder)
    {
        var raw = home.GetValue<string>(HomeSectionsAlias);
        if (string.IsNullOrWhiteSpace(raw))
        {
            var slides = contents.GetPagedChildren(sliderFolder.Id, 0, 100, out _)
                .Where(x => x.ContentType.Alias == ItemAlias && x.GetValue<string>("itemType") == "slider")
                .Select(x => new HomeSectionItem(
                    Guid.NewGuid().ToString("N"),
                    x.GetValue<string>("itemTitle") ?? x.Name,
                    x.GetValue<string>("itemText") ?? string.Empty,
                    x.GetValue<string>("itemImage") ?? string.Empty,
                    x.GetValue<string>("itemLink") ?? string.Empty,
                    x.GetValue<string>("itemLinkText") ?? "Teklif Al",
                    true))
                .ToList();
            var partners = contents.GetPagedChildren(home.Id, 0, 100, out _)
                .Where(x => x.ContentType.Alias == ItemAlias && x.GetValue<string>("itemType") == "partner")
                .Select(x => new HomeSectionItem(
                    Guid.NewGuid().ToString("N"),
                    x.GetValue<string>("itemTitle") ?? x.Name,
                    x.GetValue<string>("itemText") ?? string.Empty,
                    x.GetValue<string>("itemImage") ?? string.Empty,
                    string.Empty,
                    string.Empty,
                    false))
                .ToList();

            if (slides.Count > 0 || partners.Count > 0)
            {
                Set(home, HomeSectionsAlias, JsonSerializer.Serialize(new HomeSections(slides, partners), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                contents.SaveAndPublish(home, Array.Empty<string>(), Constants.Security.SuperUserId);
            }
        }

        // Keep the content tree clean after the content is safely stored in the home editor.
        foreach (var item in contents.GetPagedChildren(sliderFolder.Id, 0, 100, out _).Where(x => x.ContentType.Alias == ItemAlias).ToList())
            contents.MoveToRecycleBin(item, Constants.Security.SuperUserId);
        foreach (var item in contents.GetPagedChildren(home.Id, 0, 100, out _).Where(x => x.ContentType.Alias == ItemAlias && x.GetValue<string>("itemType") == "partner").ToList())
            contents.MoveToRecycleBin(item, Constants.Security.SuperUserId);
        contents.MoveToRecycleBin(sliderFolder, Constants.Security.SuperUserId);
    }

    private sealed record HomeSections(List<HomeSectionItem> Slides, List<HomeSectionItem> Partners);
    private sealed record HomeSectionItem(string Id, string Title, string Description, string Image, string Link, string LinkText, bool ShowQuote);

    private static void Set(IContent content, string alias, string value) => content.SetValue(alias, value);
    private PropertyType Text(string name, string alias) => Property(name, alias, Constants.PropertyEditors.Aliases.TextBox, ValueStorageType.Nvarchar);
    private PropertyType LongText(string name, string alias) => Property(name, alias, Constants.PropertyEditors.Aliases.TextArea, ValueStorageType.Ntext);
    private PropertyType Media(string name, string alias) => Property(name, alias, "Umbraco.MediaPicker3", ValueStorageType.Nvarchar);
    private PropertyType Property(string name, string alias, string editor, ValueStorageType storage) =>
        new(shortStrings, editor, storage) { Name = name, Alias = alias };
    private static void AddGroup(ContentType type, string name, IEnumerable<PropertyType> properties)
    {
        var group = new PropertyGroup(true) { Name = name, Alias = name.ToLowerInvariant().Replace(' ', '-') };
        foreach (var property in properties) group.PropertyTypes!.Add(property);
        type.PropertyGroups.Add(group);
    }
}
