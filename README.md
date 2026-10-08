# Fluent Legion Toolbar

أداة شخصية أصلية مبنية باستخدام WinUI 3 وMica لجهاز Lenovo Legion 5 15ITH6H (82JH). يفتح رمز منطقة الإعلام لوحة عائمة بتصميم Windows 11 وأيقونات Segoe Fluent Icons.

## حالة المشروع
نسخة أولية خاصة للتجربة. نجح بناء Windows وتشغيل أربع معاينات WinUI (عربي/إنجليزي وفاتح/داكن)، وتم فحص الصور وإصلاح قص الاتجاه العربي والتباين. لا يوجد إصدار عام. لم يُختبر التحكم على اللابتوب الفعلي بعد، ولا تثبت المعاينات ظهور Mica على سطح المكتب.

## الوظائف
- قفل Fn عبر EnergyDrv مع فحص الدعم وتأكيد الحالة.
- تبديل 60/144 هرتز فقط إن كانا متاحين للشاشة الداخلية بالدقة الحالية، دون تغيير الشاشات الخارجية.
- طاقة USB الدائمة مع فحص دعم التشغيل على البطارية. قد تستنزف البطارية، وتظل سياسات الفيرموير سارية.
- قراءة نسبة البطارية والطاقة المتصلة وحالة الشحن من Windows.
- كتم جميع الميكروفونات النشطة عبر Windows Core Audio، بما فيها الأجهزة الخارجية.
- وضع الحفاظ على البطارية والشحن السريع عبر برنامج تشغيل Lenovo EnergyDrv. الوضعان متعارضان.
- قفل لوحة اللمس عبر Lenovo WMI، بعد فحص الدعم وتأكيد منفصل لحماية المستخدم من فقدان المؤشر.
- اللغتان العربية والإنجليزية، واتجاها القراءة، والوضعان الفاتح والداكن.

لا ترسل الأداة أوامر شحن أو لوحة لمس إلى موديل مختلف. تتعطل العناصر التي لا يمكن قراءة حالتها. لا توقف Lenovo Vantage ولا تغير خدمات النظام. قد تعيد خدمات Lenovo ضبط وضع الشحن؛ تعرض الأداة فشل التأكيد بدلاً من نجاح وهمي.

## التشغيل والبناء
Windows 11 x64 و.NET 8 Desktop Runtime. من صفحة Actions الخاصة بالريبو، افتح آخر تشغيل ناجح لـ `Verify private WinUI build`، ثم نزّل `private-test-build-and-captures`. فك الملف، ثم فك `Fluent-Legion-toolbar-runtime-dependent.zip` في مجلد كامل وشغّل `FluentLegionToolbar.exe`. لا تنقل ملف exe وحده، لأن ملفات XAML والمكتبات المرافقة مطلوبة.

للبناء من المصدر استخدم Visual Studio 2022 مع MSBuild وأدوات Windows/WinUI، ثم اتبع `.github/workflows/verify.yml`. هذا هو المسار الذي تم التحقق منه؛ `dotnet publish` وحده لم ينجح في إعداد أدوات PRI أثناء التجربة. سير العمل ينسخ ملفات XBF وPRI اللازمة إلى مجلد النشر قبل ضغطه.

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

Private prototype for the Legion 5 15ITH6H. The Windows build and all four WinUI fixture captures passed; the captures were visually inspected. Laptop hardware controls and desktop Mica still need testing on the actual device.

Download the artifact from the latest successful `Verify private WinUI build` run in Actions. Unzip the artifact, then unzip `Fluent-Legion-toolbar-runtime-dependent.zip` and run `FluentLegionToolbar.exe` from the complete folder. Requires Windows 11 x64 and .NET 8 Desktop Runtime. No public release has been published.

Build from source with Visual Studio MSBuild using `.github/workflows/verify.yml`, including its compiled XAML/resource copy step. The seven controls are Fn Lock, all-active-microphone mute, conservation, rapid charge, touchpad lock, internal-panel 60/144 Hz and Always-on USB. Unsupported controls stay disabled. Conservation and rapid charge are mutually exclusive. Have an external mouse ready before testing touchpad lock. Always-on USB may drain the battery.
