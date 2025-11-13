# Case Project
Staj case projesi – duygu analizi sohbet(chat) uygulaması

🚀 Projenin Genel Mimarisi

Projede toplam dört ana bileşen bulunuyor:

1) React Web Arayüz (Vercel’de canlı)

Kullanıcı giriş ekranı

Sohbet ekranı

Mesajların anlık sentiment analizi

Basit ve hızlı bir UI

✅ Canlı link:
https://case-project-ko.vercel.app/

---

2) React Native Mobil Uygulama (APK hâliyle teslim edildi)

Android cihazlarda çalışan mobil chat ekranı

Web’daki tüm fonksiyonlarla uyumlu

Kullanıcı giriş, konuşma başlatma ve mesajlaşma desteği

AI servisinden anlık duygu analizi alma

📱 APK indirilebilir klasör:
https://drive.google.com/drive/folders/1n-HluwH6iuDi4QJBeVA4Ui7tlDQY0L5u?usp=sharing

---

3) Backend (.NET Core API – Render’da canlı)

Backend tarafında:

Kullanıcı oluşturma

Konuşma oluşturma / listeleme

Mesaj ekleme / mesaj listeleme

AI servisinden sentiment sonucu işleme

Web ve mobil arasında ortak API sağlama

API ücretsiz Render üzerinde barınıyor.

✅ API Base URL:
https://case-project-ko.onrender.com/

Backend Render üzerinde “sleep mode” yapabileceği için ilk istekte 20–30 saniyelik soğuk başlangıç olabilir.
Gün içinde uyanık kalması için UptimeRobot ile ping atılarak stabilize edilmiştir.

---

4) AI Servisi (HuggingFace Space – Python / Gradio)

AI servisi, HuggingFace üzerinden çalışan küçük bir Python modeli.
Web ve mobil uygulama her mesaj gönderildiğinde bu servise istek atarak sentiment analizini alıyor.

✅ HuggingFace Space:
https://huggingface.co/spaces/ozgur1/sentiment-analyzer

---

📂 Proje Klasör Yapısı
case-project/
│
├── frontend/
│  ├── webclient/      → React Web (Vercel’e deploy edildi)
│  └── mobileclient/   → React Native Mobil (APK üretimi bu klasörden)
├── backend/            → .NET Core API (Render’da çalışıyor)
└── ai-service/         → HuggingFace Space python servisi(dosyalar harici bağlandı bu yüzden içi boş.)


Her bir klasör kendi içerisinde bağımsız çalışabilecek şekilde yapılandırıldı.

---

⚙️ Çalıştırma Talimatları (Local)
Backend
cd backend
dotnet run

Web Client
cd frontend/webclient
npm install
npm start

Mobile Client
cd frontend/mobileclient
npm install
npx react-native start
npx react-native run-android

AI Service

HuggingFace üzerinde otomatik çalıştığı için local kuruluma gerek yok.

---

✅ Deploy Yapılan Servisler
Katman	     Servis	             Link
Web UI	     Vercel	             https://case-project-ko.vercel.app/

Backend      API	Render	       https://case-project-ko.onrender.com/

AI Servisi	 HuggingFace	       https://huggingface.co/spaces/ozgur1/sentiment-analyzer

Mobil	       Google Drive	       APK

---

🧠 AI Entegrasyonu

Frontend → Backend → HuggingFace → Backend → Frontend
şeklinde dönen bir pipeline var.

Mesaj gönderilirken:

Backend mesajı kaydediyor,

AI servisine içerik gönderiyor,

Duygu analizini alıp mesaja ekliyor,

Frontend’e sentiment + emoji olarak geri dönüyor.

---

✅ Projede Elle Yazdığım Bölümler

Backend tarafında hata düzeltmeleri
Frontend tarafında tasarım etkenleri

✅ Projede AI Yardım Aldığım Bölümler

Bazı UI düzeltmeleri (özellikle React Native tarafında)

Sentiment analiz servisinin yapısının oluşturulması

Deploy sırasında oluşan hataların giderilmesi

README formatlama ve teknik açıklamaların toparlanması

---

🙌 Son Söz

Bu proje, kısa bir süre içerisinde full-stack bir yapı kurma, servisleri farklı platformlara dağıtma ve mobil/web altyapılarını birbirine bağlama konusunda güzel bir deneyim oldu.

Tüm bileşenler çalışır durumdadır ve incelenmeye hazırdır.

Her türlü geri bildirime açığım!
