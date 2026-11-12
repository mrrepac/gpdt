# Сторонние компоненты

## PawnIO и IntelMSR

Для мощности используется обычный подписанный [PawnIO](https://github.com/namazso/PawnIO). Его установщик не включён в релиз gpdt; скачайте его с [официальной страницы PawnIO.Setup](https://github.com/namazso/PawnIO.Setup/releases/latest).

`drivers/IntelMSR.bin` — неизменённый модуль из [PawnIO.Modules 0.2.11](https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.11), copyright namazso, LGPL-2.1-or-later. Лицензия сохранена в `drivers/COPYING`. Соответствующие исходники всей версии находятся в `third_party/PawnIO.Modules-0.2.11-source.zip`.

Модуль загружается отдельным файлом. Исходники gpdt открыты для проверки и сборки; проверки подписи модуля выполняет сам PawnIO. Установщик PawnIO и его исходники распространяются отдельно его автором.

## Microsoft

.NET Framework, WinForms, Windows SDK/WDK и UMDF используются как системные компоненты и инструменты сборки. Их SDK и компиляторы не включены в релиз или Git.

## Значок

`assets/gpdt.svg`, `assets/gpdt.ico` и генератор `scripts/build-icon.ps1` созданы для этого проекта. Это собственный значок gpdt, не логотип GPD.
