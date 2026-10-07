# Fluent Legion Toolbar

أداة شخصية أصلية مبنية باستخدام WinUI 3 وMica لجهاز Lenovo Legion 5 15ITH6H (82JH). يفتح رمز منطقة الإعلام لوحة عائمة بتصميم Windows 11 وأيقونات Segoe Fluent Icons.

## حالة المشروع
نسخة أولية قيد التحقق. لا توجد نسخة منشورة. نجاح البناء لا يثبت عمل التحكم في جهاز فعلي.

## الوظائف
- قراءة نسبة البطارية والطاقة المتصلة وحالة الشحن من Windows.
- كتم جميع الميكروفونات النشطة عبر Windows Core Audio، بما فيها الأجهزة الخارجية.
- وضع الحفاظ على البطارية والشحن السريع عبر برنامج تشغيل Lenovo EnergyDrv. الوضعان متعارضان.
- قفل لوحة اللمس عبر Lenovo WMI، بعد فحص الدعم وتأكيد منفصل لحماية المستخدم من فقدان المؤشر.
- فتح إعدادات خصوصية الكاميرا. مفتاح e-shutter في هذا الجهاز فعلي، ولا تعرض الأداة حالة مزيفة له.
- اللغتان العربية والإنجليزية، واتجاها القراءة، والوضعان الفاتح والداكن.

لا ترسل الأداة أوامر شحن أو لوحة لمس إلى موديل مختلف. تتعطل العناصر التي لا يمكن قراءة حالتها. لا توقف Lenovo Vantage ولا تغير خدمات النظام. قد تعيد خدمات Lenovo ضبط وضع الشحن؛ تعرض الأداة فشل التأكيد بدلاً من نجاح وهمي.

## التشغيل والبناء
Windows 11 x64، و.NET 8 Desktop Runtime. افتح مشروع `FluentLegionToolbar.csproj` على Windows ثم:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o out
.\out\FluentLegionToolbar.exe
```

زر الإغلاق يغلق التطبيق بالكامل. النقر بعيداً عن اللوحة يخفيها مع إبقاء رمز منطقة الإعلام. يفتح النقر الأيسر اللوحة أو يخفيها، وتوفر القائمة اليمنى الفتح واللغة والخروج.

`--capture <file.png> --light|--dark --arabic` يرسم معاينة WinUI حقيقية ببيانات اختبار معلنة، ولا يلمس الجهاز. صور المعاينة ليست دليلاً على نجاح التحكم في العتاد أو إظهار Mica في جهاز المستخدم.

## التحقق على الجهاز
1. تأكد من وجود فأرة خارجية قبل تجربة قفل لوحة اللمس.
2. قارن النسبة والشحن مع Windows، ووضع البطارية مع Vantage.
3. جرّب وضع الحفاظ، ثم الشحن السريع، وتحقق أن الحالة المقروءة تتغير وأنهما لا يعملان معاً.
4. جرّب كتم الميكروفون وتحقق في إعدادات الصوت.
5. أرسل التشخيص الظاهر في تفاصيل البطارية إذا تعطلت أي وظيفة.

المطوّر: محمد النجار.

brought to you by app.instinct AI

## English
Private WinUI 3/Mica notification-area flyout for Legion 5 15ITH6H (82JH). Camera opens Windows privacy settings; the physical e-shutter is not software-controlled. Hardware controls are capability-gated and require testing on the target laptop. No public releases are published.
