# Сторонние компоненты

## PawnIO и IntelMSR

Для мощности используется обычный подписанный [PawnIO](https://github.com/namazso/PawnIO). Включён неизменённый официальный установщик [PawnIO.Setup 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0), copyright namazso. SHA-256: `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`. Установщик автора содержит разрешение: “This installer can be redistributed unmodified.” Его условия и уведомления сохранены внутри EXE. Используется только обычная подписанная редакция, без Unrestricted.

`drivers/IntelMSR.bin` — неизменённый модуль из [PawnIO.Modules 0.2.11](https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.11), copyright namazso, LGPL-2.1-or-later. Лицензия сохранена в `drivers/COPYING`. Соответствующие исходники всей версии находятся в `third_party/PawnIO.Modules-0.2.11-source.zip`.

Модуль загружается отдельным файлом. Исходники gpdt открыты для проверки и сборки; проверки подписи модуля выполняет сам PawnIO. Исходники драйвера и лицензия доступны в [репозитории автора](https://github.com/namazso/PawnIO). gpdt не изменяет и не переподписывает установщик PawnIO.

## Microsoft

.NET Framework, WinForms, Windows SDK/WDK и UMDF используются как системные компоненты и инструменты сборки. Их SDK и компиляторы не включены в релиз или Git.

## Значок

`assets/gpdt.svg`, `assets/gpdt.ico` и генератор `scripts/build-icon.ps1` созданы для этого проекта. Это собственный значок gpdt, не логотип GPD.
