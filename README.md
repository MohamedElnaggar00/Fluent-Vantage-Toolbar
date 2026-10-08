# Fluent Legion Toolbar

أداة شخصية أصلية مبنية باستخدام WinUI 3 وMica لجهاز Lenovo Legion 5 15ITH6H (82JH). يفتح رمز منطقة الإعلام لوحة عائمة بتصميم Windows 11 وأيقونات Segoe Fluent Icons.

## حالة المشروع
نسخة أولية خاصة للتجربة. نجح بناء Windows وتشغيل أربع معاينات WinUI (عربي/إنجليزي وفاتح/داكن)، وتم فحص الصور وإصلاح قص الاتجاه العربي والتباين. لا يوجد إصدار عام. لم يُختبر التحكم على اللابتوب الفعلي بعد، ولا تثبت المعاينات ظهور Mica على سطح المكتب.

## ما الجديد في 0.2.0
- أيقونة Lenovo Legion الجديدة للتطبيق ولرمز منطقة الإعلام.
- قائمة النقر الأيمن: فتح، إعدادات الشريط، حول.
- صفحة إعدادات (أيقونة الترس أعلى اللوحة): اللغة، السمة، إظهار وإخفاء أزرار الواجهة الرئيسية، وإظهار وإخفاء رابطي «تفاصيل البطارية» و«خيارات الضمان». أُزيل اختيار اللغة والسمة من الواجهة الرئيسية.
- صفحة تفاصيل البطارية: الشحن الحالي، صحة البطارية، السعة التصميمية، سعة الشحن الكامل، عدد الدورات، تاريخ التصنيع.
- بطاقة الضمان: الحالة، تاريخ البدء والانتهاء، الأيام المتبقية، رابط دعم Lenovo. تُقرأ من خادم دعم Lenovo باستخدام الرقم التسلسلي للجهاز، وتُحفظ نسخة محلية.
- لون البطارية: أخضر، ثم أصفر عند 20% أو أقل، ثم أحمر عند 5% أو أقل.
- إصلاح قفل Fn بنفس مسار التحكم في LenovoLegionToolkit، وتصميم جديد للأيقونة.
- النافذة لا تظهر في شريط المهام، وتعمل كنافذة أداة من رمز منطقة الإعلام.
- تشغيل في الخلفية بصلاحيات المسؤول بدون نافذة UAC عبر Task Scheduler (انظر قسم التشغيل التلقائي).

## التشغيل التلقائي بصلاحيات المسؤول (SkipUAC)
1. فك الملف المضغوط كاملاً في مجلد ثابت لا تنقله لاحقاً.
2. شغّل `install-startup.ps1` بزر الفأرة الأيمن ثم «Run with PowerShell». تطلب موافقة المسؤول مرة واحدة فقط.
3. ينشئ السكربت مهمتين: `FluentLegionToolbar` تعمل عند تسجيل الدخول بوضع الخلفية وبأعلى الصلاحيات، و`FluentLegionToolbar-Open` تفتح اللوحة. بعدها يبدأ التطبيق مع كل دخول بدون نافذة UAC، وأي تشغيل عادي للملف التنفيذي يمرّر الطلب إلى المهمة تلقائياً.
4. للإزالة شغّل `uninstall-startup.ps1`.

ملاحظة: يعمل التطبيق كعملية خلفية دائمة في منطقة الإعلام وليس كخدمة Windows، لأن واجهة WinUI لا تعمل داخل جلسة الخدمات.

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

زر الإغلاق يغلق التطبيق بالكامل. النقر بعيداً عن اللوحة يخفيها مع إبقاء رمز منطقة الإعلام. يفتح النقر الأيسر اللوحة أو يخفيها، وتوفر القائمة اليمنى: فتح، إعدادات الشريط، حول. زر «إنهاء البرنامج» داخل صفحة الإعدادات.

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

Version 0.2.0: Legion logo as app and tray icon; right-click menu (Open, Toolbar settings, About); settings page behind a gear icon (language, theme, per-button and link visibility); battery details page; warranty card read from Lenovo support with the machine serial number (cached locally); battery colour green, yellow at 20% or less, red at 5% or less; Fn Lock fixed using the same EnergyDrv control path as LenovoLegionToolkit, new icon; flyout no longer appears in the taskbar; background instance started at logon through a highest-privilege scheduled task (run `install-startup.ps1` once; `uninstall-startup.ps1` removes it). The background part is a resident tray process, not a Windows service, because WinUI cannot run in the services session.

Private prototype for the Legion 5 15ITH6H. The Windows build and all four WinUI fixture captures passed; the captures were visually inspected. Laptop hardware controls and desktop Mica still need testing on the actual device.

Download the artifact from the latest successful `Verify private WinUI build` run in Actions. Unzip the artifact, then unzip `Fluent-Legion-toolbar-runtime-dependent.zip` and run `FluentLegionToolbar.exe` from the complete folder. Requires Windows 11 x64 and .NET 8 Desktop Runtime. No public release has been published.

Build from source with Visual Studio MSBuild using `.github/workflows/verify.yml`, including its compiled XAML/resource copy step. The seven controls are Fn Lock, all-active-microphone mute, conservation, rapid charge, touchpad lock, internal-panel 60/144 Hz and Always-on USB. Unsupported controls stay disabled. Conservation and rapid charge are mutually exclusive. Have an external mouse ready before testing touchpad lock. Always-on USB may drain the battery.

