# 14. Упаковка и использование в других проектах

Глава о том, как распространять библиотеку: собрать NuGet-пакеты, подключить их
к другому решению на этой же машине, в локальной сети или через сервер пакетов.

## 14.1. Способы подключения: что выбрать

| Способ | Когда подходит | Плюсы | Минусы |
|---|---|---|---|
| `ProjectReference` на `src/Mvk.Modbus` и `src/Mvk.MVK01` | Проект рядом (в том же решении, монорепозиторий, правки библиотеки одновременно) | мгновенная сборка, отладка по шагам в исходники | путь жёстко привязан к структуре каталогов; нет версионирования |
| Локальная папка с `.nupkg` (локальный фид) | Другие решения на этой машине или на компьютерах в сети (папка на сетевом диске) | просто, версионируется, работает без интернета | вручную обновлять пакеты |
| Внутренний NuGet-сервер (Azure Artifacts, Artifactory, BaGet, Nexus) | Команда из нескольких человек | централизованно, доступы, CI | нужен сервер |
| nuget.org | Публичная библиотека | доступна всем | публикация наружу, нужна лицензия |

Ниже — первые два способа (локальная разработка и локальный фид), затем кратко
публикация на сервер.

## 14.2. Что уже настроено для упаковки

Метаданные заданы в корневом `Directory.Build.props` — они применяются ко всем проектам:

```xml
<VersionPrefix Condition="'$(VersionPrefix)' == ''">0.1.0</VersionPrefix>
<Authors>БеляевИА</Authors>
<Product>ConnectMVKBulder</Product>
<PackageTags>modbus;modbus-tcp;mvk;mvk01;виброконтроль</PackageTags>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
<PackageReadmeFile>README.md</PackageReadmeFile>
<RepositoryType>git</RepositoryType>
```

В проектах библиотек дополнительно:

```xml
<PackageId>Mvk.Modbus</PackageId>        <!-- и Mvk.MVK01 -->
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```

В тестах и примерах стоит `<IsPackable>false</IsPackable>` — они не попадают в пакеты.

Что это даёт:

- `dotnet pack` создаёт пакеты **Mvk.Modbus** и **Mvk.MVK01**;
- внутрь попадает XML-документация (IntelliSense в чужом проекте показывает описания
  русских комментариев);
- `Mvk.MVK01` автоматически зависит от `Mvk.Modbus` **той же версии** — потребитель
  ставит один пакет, второй подтягивается сам;
- рядом с `.nupkg` создаётся `.snupkg` (символы) для пошаговой отладки потребителем.

## 14.3. Сборка пакетов

```powershell
dotnet pack ConnectMVKBulder.sln -c Release -o nupkgs
```

Результат в папке `nupkgs/`:

```
Mvk.Modbus.0.1.0.nupkg      Mvk.Modbus.0.1.0.snupkg
Mvk.MVK01.0.1.0.nupkg       Mvk.MVK01.0.1.0.snupkg
```

Состав пакета `Mvk.MVK01.0.1.0.nupkg` (проверено):

```
lib/net8.0/Mvk.MVK01.dll
lib/net8.0/Mvk.MVK01.xml        — XML-документация
README.md                        — отображается на странице пакета
Mvk.MVK01.nuspec                 — зависимости: Mvk.Modbus 0.1.0, System.Text.Encoding.CodePages
```

Задать версию для конкретной сборки можно без правки файлов:

```powershell
dotnet pack ConnectMVKBulder.sln -c Release -o nupkgs -p:Version=0.2.0
```

> Папка `nupkgs/` — артефакт сборки; добавьте её в `.gitignore` (пакеты не хранят в git).

## 14.4. Установка в другом проекте

### Вариант 1. Разовое подключение из папки

```powershell
dotnet add C:\Projects\MyApp\MyApp.csproj package Mvk.MVK01 `
    --version 0.1.0 --source C:\Libraries\ConnectMVKBulder\nupkgs
```

`Mvk.Modbus` установится автоматически как зависимость. Если нужен только транспорт
без типизированного слоя МВК01 — ставьте только `Mvk.Modbus`.

### Вариант 2. Постоянный локальный источник

```powershell
dotnet nuget add source C:\Libraries\ConnectMVKBulder\nupkgs -n mvk-local
dotnet add C:\Projects\MyApp\MyApp.csproj package Mvk.MVK01 --version 0.1.0
```

Проверить и удалить источник:

```powershell
dotnet nuget list source
dotnet nuget remove source mvk-local
```

Папку можно положить на сетевой диск (`\\server\packages`) — тогда ей смогут
пользоваться другие машины.

### Вариант 3. Visual Studio

`Сервис → Параметры → NuGet Package Manager → Package Sources → +` — укажите путь
к папке `nupkgs` и поставьте её первой в списке. Далее обычное «Управление
пакетами NuGet» → `Mvk.MVK01`.

### Проверка после установки

```powershell
dotnet list C:\Projects\MyApp\MyApp.csproj package
```

В списке должны быть `Mvk.MVK01` и `Mvk.Modbus` с ожидаемой версией.

## 14.5. Использование в чужом проекте (тот же код)

После установки пакета код не отличается от примеров руководства:

```csharp
using Mvk.MVK01;

await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
Mvk01Channel channel = await mvk.ReadChannelAsync(1);
Console.WriteLine($"{channel.SensorVoltage:F4} В, СКЗ 10…1000 Гц: {channel.RmsSets[2].Acceleration:F4} м/с2");
```

Для DI — как обычно: `AddMvkModbus` + `AddMvk01Devices` (см. главы 03 и 10).
Транзитивные зависимости (`NModbus`, `Microsoft.Extensions.*`,
`System.Text.Encoding.CodePages`) подключаются NuGet автоматически.

## 14.6. Обновление версии

Рекомендуемый цикл:

1. увеличить версию (в `Directory.Build.props` или параметром `-p:Version=`);
2. собрать пакеты: `dotnet pack ConnectMVKBulder.sln -c Release -o nupkgs`;
3. обновить пакет у потребителя:

```powershell
dotnet add C:\Projects\MyApp\MyApp.csproj package Mvk.MVK01 --version 0.2.0
```

> **Важно.** NuGet кэширует пакеты по версии: если пересобрать ту же версию,
> потребитель может получить старое содержимое из кэша. Всегда повышайте версию.
> Принудительно очистить кэш: `dotnet nuget locals all --clear`.

## 14.7. Публикация на сервер или nuget.org

Если нужен общий фид — публикация одной командой:

```powershell
# nuget.org
dotnet nuget push nupkgs\Mvk.Modbus.0.1.0.nupkg --api-key <API_KEY> --source https://api.nuget.org/v3/index.json
dotnet nuget push nupkgs\Mvk.MVK01.0.1.0.nupkg  --api-key <API_KEY> --source https://api.nuget.org/v3/index.json

# внутренний сервер (пример)
dotnet nuget push nupkgs\*.nupkg --source https://nuget.company.local/v3/index.json --api-key <TOKEN>
```

Перед публикацией наружу стоит добавить в `Directory.Build.props`:

```xml
<PackageLicenseExpression>MIT</PackageLicenseExpression>   <!-- или ваша лицензия -->
<PackageProjectUrl>https://example.com/connect-mvk</PackageProjectUrl>
<PackageIcon>icon.png</PackageIcon>
```

и положить файл `LICENSE` (для MIT) в корень репозитория.

Символы (`.snupkg`) публикуются тем же способом — `dotnet nuget push nupkgs\Mvk.Modbus.0.1.0.snupkg`.

## 14.8. Сборка в CI

Минимальный шаг для любой CI-системы (GitHub Actions, GitLab CI, Azure Pipelines):

```powershell
dotnet restore ConnectMVKBulder.sln
dotnet build   ConnectMVKBulder.sln -c Release --no-restore
dotnet test    ConnectMVKBulder.sln -c Release --no-build
dotnet pack    ConnectMVKBulder.sln -c Release --no-build -o nupkgs -p:Version=1.2.3
```

Папку `nupkgs` сохраните как артефакт сборки и передайте в шаг публикации.

## 14.9. Проектная ссылка вместо пакета

Если проект-потребитель лежит рядом и пакет не нужен:

```xml
<ItemGroup>
  <ProjectReference Include="C:\Libraries\ConnectMVKBulder\src\Mvk.MVK01\Mvk.MVK01.csproj" />
</ItemGroup>
```

Достаточно одной ссылки на `Mvk.MVK01` — `Mvk.Modbus` подтянется транзитивно.
Этот способ удобен при отладке библиотеки: точка останова внутри `Mvk.MVK01`
сработает прямо из вашего кода.

## 14.10. Подводные камни

- **Кэш NuGet по версии.** Пересобрали пакет без смены версии — потребитель увидит
  старое. Меняйте версию или очищайте кэш (`dotnet nuget locals all --clear`).
- **`--source` нужен, если локальный фид не добавлен.** Без него `dotnet add package`
  ищет пакет только в nuget.org и выдаёт `NU1101`.
- **Версии `Mvk.Modbus` и `Mvk.MVK01` должны совпадать.** Пакет `Mvk.MVK01`
  зависит от точной версии `Mvk.Modbus`; публикуйте оба пакета вместе.
- **Тесты и примеры не пакуются** — у них `IsPackable=false`; это ожидаемо.
- **XML-документация требует сборки Release без предупреждений CS1591** — следите
  за `dotnet build` (в репозитории предупреждений нет).
- **Кириллица в метаданных** хранится в UTF-8; консоль PowerShell может отображать
  её некорректно, но в самом пакете текст правильный (проверено распаковкой nuspec).
- **Символы нужны для отладки потребителем.** Если `.snupkg` не публикуются, шаги
  в библиотечные методы будут недоступны.

## См. также

- [Расширение и интеграция](10-extensibility.md) — DI, WPF, ASP.NET, фоновые службы.
- [Конфигурация](03-configuration.md) — `appsettings.json` и `mvkmodbus.json`.
- [README](../README.md) — команды сборки и запуск примеров.
