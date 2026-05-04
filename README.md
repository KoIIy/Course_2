# Course

Учебный проект для обработки и сравнения анкет. В репозитории находятся два
консольных приложения на C# и набор SQL-скриптов для подготовки базы данных.

## Состав проекта

```text
PDFtoPNG/
  PDFtoPNG/            Конвертер PDF-страниц в PNG
sql_server_work/       Скрипты создания базы данных и таблиц SQL Server
TestOpenCV/
  TestOpenCV/          Сравнение фото документа с эталонным изображением
```

## Технологии

- C# и .NET `net10.0`
- `Docnet.Core` для рендеринга PDF через PDFium
- `OpenCvSharp4` для обработки изображений и поиска отличий
- SQL Server для хранения шаблонов, страниц и областей анкеты

## PDFtoPNG

`PDFtoPNG` конвертирует PDF-файл или все PDF-файлы из папки в набор PNG-страниц.
По умолчанию используется качество `400 DPI`.

### Запуск

Из корня репозитория:

```powershell
dotnet run --project .\PDFtoPNG\PDFtoPNG\PDFtoPNG.csproj -- "путь\к\файлу.pdf"
```

С указанием папки вывода и DPI:

```powershell
dotnet run --project .\PDFtoPNG\PDFtoPNG\PDFtoPNG.csproj -- "путь\к\файлу.pdf" "путь\к\папке_вывода" --dpi 300
```

Если передать папку вместо файла, приложение обработает все PDF-файлы в этой
папке:

```powershell
dotnet run --project .\PDFtoPNG\PDFtoPNG\PDFtoPNG.csproj -- "путь\к\папке_pdf" "путь\к\папке_вывода"
```

Результат сохраняется в виде файлов:

```text
page_001.png
page_002.png
page_003.png
```

Если папка вывода не указана, рядом с PDF создается папка `<имя_pdf>_png`.

## TestOpenCV

`TestOpenCV` сравнивает фотографию заполненного документа с эталонной страницей.
Приложение:

- читает `test.jpg` как фото документа;
- читает `test123.png` как эталон;
- выравнивает фото по эталону через SIFT, BFMatcher и гомографию;
- удаляет линии и текст шаблона;
- отдельно обрабатывает чекбоксы;
- сохраняет маску отличий и итоговое изображение с красной подсветкой.

### Подготовка входных файлов

Перед запуском в рабочей папке должны лежать:

```text
test.jpg      фото заполненного документа
test123.png   эталонная страница
```

В текущем проекте пример таких файлов уже находится в папке сборки:

```text
TestOpenCV\TestOpenCV\bin\Debug\net10.0\
```

### Запуск без окна просмотра

```powershell
cd .\TestOpenCV\TestOpenCV\bin\Debug\net10.0
.\TestOpenCV.exe --no-window
```

После запуска создается папка `output` со следующими файлами:

```text
aligned_document.png    выровненное фото документа
difference_mask.png     бинарная маска найденных отличий
comparison_result.png   итоговое изображение с подсветкой
```

### Запуск через dotnet

```powershell
dotnet run --project .\TestOpenCV\TestOpenCV\TestOpenCV.csproj -- --no-window
```

При таком запуске `test.jpg` и `test123.png` должны находиться в текущей рабочей
папке, из которой выполняется команда.

## База данных

Папка `sql_server_work` содержит скрипты для SQL Server:

- `01_create_database.sql` создает базу `AnketaOK`, если она еще не существует.
- `02_create_tables.sql` создает таблицы для шаблонов, страниц, обязательных
  областей, чекбоксов и загруженных анкет.

Запуск через `sqlcmd`:

```powershell
sqlcmd -S localhost -E -b -i .\sql_server_work\01_create_database.sql
sqlcmd -S localhost -E -b -d AnketaOK -i .\sql_server_work\02_create_tables.sql
```

Для LocalDB можно использовать:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i .\sql_server_work\01_create_database.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -d AnketaOK -i .\sql_server_work\02_create_tables.sql
```

## Сборка

```powershell
dotnet restore .\PDFtoPNG\PDFtoPNG\PDFtoPNG.csproj
dotnet build .\PDFtoPNG\PDFtoPNG\PDFtoPNG.csproj

dotnet restore .\TestOpenCV\TestOpenCV\TestOpenCV.csproj
dotnet build .\TestOpenCV\TestOpenCV\TestOpenCV.csproj
```

Для `TestOpenCV` используется `PlatformTarget=x64`, потому что нативные
библиотеки OpenCV подключены для Windows x64.

## Примечания

- Папки `bin` и `obj` являются результатами сборки.
- `PDFtoPNG` удаляет только ранее сгенерированные файлы вида `page_*.png` в
  папке вывода.
- `TestOpenCV` сейчас использует фиксированные имена входных файлов:
  `test.jpg` и `test123.png`.
