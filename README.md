# Exen Solar web ve yönetim paneli

Bu proje, `energix` içindeki statik sayfa tasarımını kullanır ve özel hazırlanmış sade bir yönetim ekranı sunar. Umbraco artık uygulamanın parçası değildir. Sayfa ve içerik verileri Microsoft SQL Server'daki `dbo.ExenSolarSiteState` tablosunda saklanır; ziyaretçi mesajları `dbo.ExenSolarMessages` tablosunda tutulur ve yeni görseller `wwwroot/uploads` altına yüklenir. Kaynak `energix` klasörü değiştirilmeden korunmuştur.

## Çalıştırma

1. .NET 10 SDK'yı kullanın: `/usr/local/share/dotnet/dotnet --version`.
2. Yerel SQL Server'ı başlatın: `docker start exensolar-mssql`.
3. `appsettings.Local.json` içindeki SQL Server bağlantı bilgisini ve `Umbraco:CMS:Unattended:UnattendedUserPassword` değerini kendi yerel ortamınıza göre ayarlayın. Bu dosya kaynak kontrolüne eklenmemelidir.
4. Bu klasörde `/usr/local/share/dotnet/dotnet run --no-launch-profile --urls http://localhost:5187` çalıştırın.
5. Site: `http://localhost:5187` · Yönetim: `http://localhost:5187/admin`.

Yönetim ekranı kullanıcı adı ve parola ile korunur. `ExenSolar:AdminUsername` ve `ExenSolar:AdminPassword` ayarlarını kullanın; eski yerel ayarlarla uyumluluk için kullanıcı adı ve parola `Umbraco:CMS:Unattended:UnattendedUserName` / `UnattendedUserPassword` değerlerinden de okunabilir. Dağıtım öncesinde güçlü ve gizli bir parola belirleyin ve HTTPS kullanın.

## Panelde düzenlenen alanlar

- Ana Sayfa: giriş metinleri, kayan resimler ve çözüm ortakları
- Hakkımızda: sayfa metni ve üst üste kullanılan iki ayrı görsel
- Hizmetlerimiz, Referanslar ve Medya: kayıt ekleme, düzenleme, silme ve görsel yükleme
- İletişim: e-posta, telefon (isteğe bağlı) ve adres
- Site logosu ve footer logosu

İletişim formu mesajları ve teklif talepleri yönetim panelindeki ayrı menülerden görüntülenebilir, okundu olarak işaretlenebilir ve silinebilir. Yeni kayıtlar okunana kadar “Yeni” etiketi taşır. Her IP için iki form uç noktasında birlikte 10 dakikada en fazla 5 gönderim kabul edilir; ayrıca honeypot ve imzalı zaman doğrulaması uygulanır. Bu korumalar spam riskini azaltır; herkese açık canlı dağıtımda ters vekil (reverse proxy) IP yönlendirmesi doğru yapılandırılmalı, daha yoğun saldırı riskinde CDN/WAF veya CAPTCHA eklenmelidir.

SQL şeması ve başlangıç içeriği ilk panel/API isteğinde oluşturulur. SQL Server bağlantısı çalışmıyorsa yönetim verisi yüklenmez; SQLite yedeği kullanılmaz. Formlar mesajları veritabanında saklar; e-posta bildirimi için ayrıca SMTP yapılandırması gerekir.
