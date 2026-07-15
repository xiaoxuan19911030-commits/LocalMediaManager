# Legacy database dictionary

Generated: 2026-07-15T12:57:13.0228869+08:00

This report was generated from the real SQLite schema and real row/null counts. Both legacy databases were opened read-only.

## business database

- Path: `D:\Jvedio\Jvedio5.0\data\Administrator\app_datas.sqlite`
- SHA-256: `5e9f0b56f54de301433a7828832b5c692893bde8b6665bb1c6ba76da452ba09a`
- Size: 11005952 bytes
- Tables: 23

### `actor_info` (1667 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `ActorID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `ActorName` | `VARCHAR(500)` | no | no | `` | 0 | 1 |
| `Country` | `VARCHAR(500)` | no | no | `` | 804 | 863 |
| `Nation` | `VARCHAR(500)` | no | no | `` | 804 | 863 |
| `BirthPlace` | `VARCHAR(500)` | no | no | `` | 804 | 863 |
| `Birthday` | `VARCHAR(100)` | no | no | `` | 804 | 863 |
| `Age` | `INT` | no | no | `` | 0 | 0 |
| `BloodType` | `VARCHAR(100)` | no | no | `` | 804 | 863 |
| `Height` | `INT` | no | no | `` | 0 | 0 |
| `Weight` | `INT` | no | no | `` | 0 | 0 |
| `Gender` | `INT` | no | no | `0` | 0 | 0 |
| `Hobby` | `VARCHAR(500)` | no | no | `` | 804 | 863 |
| `Cup` | `VARCHAR(1)` | no | no | `'Z'` | 0 | 0 |
| `Chest` | `INT` | no | no | `` | 0 | 0 |
| `Waist` | `INT` | no | no | `` | 0 | 0 |
| `Hipline` | `INT` | no | no | `` | 0 | 0 |
| `WebType` | `VARCHAR(100)` | no | no | `` | 804 | 863 |
| `WebUrl` | `VARCHAR(2000)` | no | no | `` | 804 | 863 |
| `Grade` | `FLOAT` | no | no | `0.0` | 0 | 0 |
| `ExtraInfo` | `TEXT` | no | no | `` | 804 | 863 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 863 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 863 |
| `ImageUrl` | `TEXT` | no | no | `` | 0 | 369 |

Foreign keys: none declared.

- Index `actor_info_idx_ActorName` (ActorName), unique: False

```sql
CREATE TABLE actor_info( ActorID INTEGER PRIMARY KEY autoincrement, ActorName VARCHAR(500), Country VARCHAR(500), Nation VARCHAR(500), BirthPlace VARCHAR(500), Birthday VARCHAR(100), Age INT, BloodType VARCHAR(100), Height INT, Weight INT, Gender INT DEFAULT 0, Hobby VARCHAR(500), Cup VARCHAR(1) DEFAULT 'Z', Chest INT, Waist INT, Hipline INT, WebType  VARCHAR(100), WebUrl  VARCHAR(2000), Grade FLOAT DEFAULT 0.0, ExtraInfo TEXT, CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')) , ImageUrl TEXT)
```

### `app_databases` (2 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `DBId` | `INTEGER` | yes | no | `` | 0 | 0 |
| `Name` | `VARCHAR(500)` | no | no | `` | 0 | 0 |
| `Count` | `INTEGER` | no | no | `0` | 0 | 0 |
| `DataType` | `INT` | no | no | `0` | 0 | 0 |
| `ImagePath` | `TEXT` | no | no | `''` | 0 | 2 |
| `ViewCount` | `INT` | no | no | `0` | 0 | 0 |
| `Hide` | `INT` | no | no | `0` | 0 | 0 |
| `ScanPath` | `TEXT` | no | no | `` | 0 | 0 |
| `ExtraInfo` | `TEXT` | no | no | `` | 0 | 2 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

- Index `type_idx` (DataType), unique: False
- Index `name_idx` (Name), unique: False

```sql
CREATE TABLE app_databases ( DBId INTEGER PRIMARY KEY autoincrement, Name VARCHAR(500), Count INTEGER DEFAULT 0, DataType INT DEFAULT 0, ImagePath TEXT DEFAULT '', ViewCount INT DEFAULT 0, Hide INT DEFAULT 0, ScanPath TEXT, ExtraInfo TEXT, CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')) )
```

### `common_ai_face` (0 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `AIId` | `INTEGER` | yes | no | `` | 0 | 0 |
| `Age` | `INT` | no | no | `0` | 0 | 0 |
| `Beauty` | `FLOAT` | no | no | `0` | 0 | 0 |
| `Expression` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `FaceShape` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `Gender` | `INT` | no | no | `0` | 0 | 0 |
| `Glasses` | `INT` | no | no | `0` | 0 | 0 |
| `Race` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `Emotion` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `Mask` | `INT` | no | no | `0` | 0 | 0 |
| `Platform` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `ExtraInfo` | `TEXT` | no | no | `` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

Indexes: none declared.


```sql
CREATE TABLE common_ai_face ( AIId INTEGER PRIMARY KEY autoincrement, Age INT DEFAULT 0, Beauty FLOAT DEFAULT 0, Expression VARCHAR(100), FaceShape VARCHAR(100), Gender INT DEFAULT 0, Glasses INT DEFAULT 0, Race VARCHAR(100), Emotion VARCHAR(100), Mask INT DEFAULT 0, Platform VARCHAR(100), ExtraInfo TEXT, CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')) )
```

### `common_association` (0 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `AID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `MainDataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `SubDataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `AssociationType` | `INT` | no | no | `0` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

- Index `common_association_idx_MainDataID_SubDataID` (MainDataID, SubDataID), unique: False
- Index `common_association_idx_SubDataID` (SubDataID), unique: False
- Index `common_association_idx_MainDataID` (MainDataID), unique: False
- Index `sqlite_autoindex_common_association_1` (MainDataID, SubDataID, AssociationType), unique: True

```sql
CREATE TABLE common_association ( AID INTEGER PRIMARY KEY autoincrement, MainDataID INTEGER, SubDataID INTEGER, AssociationType INT DEFAULT 0, CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), unique(MainDataID,SubDataID,AssociationType) )
```

### `common_deleted_rating_memory` (2 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `FileName` | `TEXT` | yes | no | `` | 0 | 0 |
| `Grade` | `FLOAT` | no | no | `0.0` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

- Index `common_deleted_rating_memory_idx_FileName` (FileName), unique: False
- Index `sqlite_autoindex_common_deleted_rating_memory_1` (FileName), unique: True

```sql
CREATE TABLE common_deleted_rating_memory (FileName TEXT PRIMARY KEY, Grade FLOAT DEFAULT 0.0, UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')))
```

### `common_images` (0 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `ImageID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `Name` | `VARCHAR(500)` | no | no | `` | 0 | 0 |
| `Path` | `VARCHAR(1000)` | no | no | `` | 0 | 0 |
| `PathType` | `INT` | no | no | `0` | 0 | 0 |
| `Ext` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `Size` | `INTEGER` | no | no | `` | 0 | 0 |
| `Height` | `INT` | no | no | `` | 0 | 0 |
| `Width` | `INT` | no | no | `` | 0 | 0 |
| `Url` | `TEXT` | no | no | `` | 0 | 0 |
| `ExtraInfo` | `TEXT` | no | no | `` | 0 | 0 |
| `Source` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

- Index `sqlite_autoindex_common_images_1` (PathType, Path), unique: True

```sql
CREATE TABLE common_images( ImageID INTEGER PRIMARY KEY autoincrement,  Name VARCHAR(500), Path VARCHAR(1000), PathType INT DEFAULT 0, Ext VARCHAR(100), Size INTEGER, Height INT, Width INT,  Url TEXT, ExtraInfo TEXT, Source VARCHAR(100),  CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')),  unique(PathType,Path) )
```

### `common_magnets` (0 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `MagnetID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `MagnetLink` | `VARCHAR(40)` | no | no | `` | 0 | 0 |
| `TorrentUrl` | `VARCHAR(2000)` | no | no | `` | 0 | 0 |
| `DataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `VID` | `INTEGER` | no | no | `` | 0 | 0 |
| `Title` | `TEXT` | no | no | `` | 0 | 0 |
| `Size` | `INTEGER` | no | no | `0` | 0 | 0 |
| `Releasedate` | `VARCHAR(10)` | no | no | `'1900-01-01'` | 0 | 0 |
| `Tag` | `TEXT` | no | no | `` | 0 | 0 |
| `DownloadNumber` | `INT` | no | no | `0` | 0 | 0 |
| `ExtraInfo` | `TEXT` | no | no | `` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | yes | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | yes | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

- Index `common_magnets_idx_VID` (VID), unique: False
- Index `common_magnets_idx_DataID` (DataID), unique: False
- Index `sqlite_autoindex_common_magnets_1` (MagnetLink), unique: True

```sql
CREATE TABLE common_magnets ( MagnetID INTEGER PRIMARY KEY autoincrement, MagnetLink VARCHAR(40), TorrentUrl VARCHAR(2000), DataID INTEGER, VID INTEGER, Title TEXT, Size INTEGER DEFAULT 0, Releasedate VARCHAR(10) DEFAULT '1900-01-01', Tag TEXT, DownloadNumber INT DEFAULT 0, ExtraInfo TEXT, CreateDate VARCHAR(30) NOT NULL DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) NOT NULL DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), unique(MagnetLink) )
```

### `common_picture_exist` (7529 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `id` | `INTEGER` | yes | no | `` | 0 | 0 |
| `DataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `PathType` | `INT` | no | no | `0` | 0 | 0 |
| `ImageType` | `INT` | no | no | `0` | 0 | 0 |
| `Exist` | `INT` | no | no | `0` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

- Index `common_picture_exist_idx_DataID_PathType_ImageType` (DataID, PathType, ImageType), unique: False
- Index `sqlite_autoindex_common_picture_exist_1` (DataID, PathType, ImageType, Exist), unique: True

```sql
CREATE TABLE common_picture_exist ( id INTEGER PRIMARY KEY autoincrement, DataID INTEGER, PathType INT DEFAULT 0, ImageType INT DEFAULT 0, Exist INT DEFAULT 0, CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), unique(DataID,PathType,ImageType,Exist) )
```

### `common_play_history` (592 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `HistoryID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `DataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `DBId` | `INTEGER` | no | no | `` | 0 | 0 |
| `DataType` | `INT` | no | no | `0` | 0 | 0 |
| `Title` | `TEXT` | no | no | `` | 1 | 193 |
| `Path` | `TEXT` | no | no | `` | 0 | 0 |
| `PlayDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

- Index `common_play_history_idx_PlayDate` (PlayDate), unique: False
- Index `common_play_history_idx_DBId_DataType_PlayDate` (DBId, DataType, PlayDate), unique: False
- Index `common_play_history_idx_DataID_PlayDate` (DataID, PlayDate), unique: False

```sql
CREATE TABLE common_play_history (HistoryID INTEGER PRIMARY KEY autoincrement, DataID INTEGER, DBId INTEGER, DataType INT DEFAULT 0, Title TEXT, Path TEXT, PlayDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')))
```

### `common_search_histories` (6 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `ID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `SearchMode` | `INT` | no | no | `0` | 0 | 0 |
| `SearchField` | `VARCHAR(200)` | no | no | `` | 0 | 0 |
| `SearchValue` | `TEXT` | no | no | `` | 0 | 0 |
| `CreateYear` | `INT` | no | no | `2022` | 0 | 0 |
| `CreateMonth` | `INT` | no | no | `7` | 0 | 0 |
| `CreateDay` | `INT` | no | no | `3` | 0 | 0 |
| `ExtraInfo` | `TEXT` | no | no | `` | 6 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `TypeMode` | `INT` | no | no | `0` | 0 | 0 |

Foreign keys: none declared.

- Index `common_search_histories_idx_SearchMode_SearchField_CreateDate` (SearchMode, SearchField, CreateDate), unique: False
- Index `common_search_histories_idx_SearchMode_SearchField_CreateYear_CreateMonth_CreateDay` (SearchMode, SearchField, CreateYear, CreateMonth, CreateDay), unique: False

```sql
CREATE TABLE common_search_histories ( ID INTEGER PRIMARY KEY autoincrement, SearchMode INT DEFAULT 0, SearchField VARCHAR(200), SearchValue TEXT, CreateYear INT DEFAULT 2022, CreateMonth INT DEFAULT 7, CreateDay INT DEFAULT 3, ExtraInfo TEXT, CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')) , TypeMode INT DEFAULT 0)
```

### `common_tagstamp` (2 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `TagID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `Foreground` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `Background` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `TagName` | `VARCHAR(200)` | no | no | `` | 0 | 0 |
| `ExtraInfo` | `TEXT` | no | no | `` | 2 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

Indexes: none declared.


```sql
CREATE TABLE common_tagstamp ( TagID INTEGER PRIMARY KEY autoincrement, Foreground VARCHAR(100), Background VARCHAR(100), TagName VARCHAR(200), ExtraInfo TEXT, CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')) )
```

### `common_transaltions` (0 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `TransaltionID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `SourceLang` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `TargetLang` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `SourceText` | `TEXT` | no | no | `` | 0 | 0 |
| `TargetText` | `TEXT` | no | no | `` | 0 | 0 |
| `Platform` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

Indexes: none declared.


```sql
CREATE TABLE common_transaltions( TransaltionID INTEGER PRIMARY KEY autoincrement,  SourceLang VARCHAR(100), TargetLang VARCHAR(100), SourceText TEXT, TargetText TEXT, Platform VARCHAR(100),  CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')) )
```

### `common_translations` (0 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `TranslationID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `SourceLang` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `TargetLang` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `SourceText` | `TEXT` | no | no | `` | 0 | 0 |
| `TargetText` | `TEXT` | no | no | `` | 0 | 0 |
| `Platform` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

Indexes: none declared.


```sql
CREATE TABLE common_translations( TranslationID INTEGER PRIMARY KEY autoincrement,  SourceLang VARCHAR(100), TargetLang VARCHAR(100), SourceText TEXT, TargetText TEXT, Platform VARCHAR(100),  CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')) )
```

### `common_url_code` (726 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `CodeId` | `INTEGER` | yes | no | `` | 0 | 0 |
| `LocalValue` | `VARCHAR(500)` | no | no | `` | 0 | 0 |
| `ValueType` | `VARCHAR(20)` | no | no | `'video'` | 0 | 0 |
| `RemoteValue` | `VARCHAR(100)` | no | no | `` | 0 | 27 |
| `WebType` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

- Index `common_url_code_idx_ValueType_WebType_LocalValue` (ValueType, WebType, LocalValue), unique: False
- Index `sqlite_autoindex_common_url_code_1` (ValueType, WebType, LocalValue, RemoteValue), unique: True

```sql
CREATE TABLE common_url_code ( CodeId INTEGER PRIMARY KEY autoincrement, LocalValue VARCHAR(500), ValueType  VARCHAR(20) DEFAULT 'video', RemoteValue VARCHAR(100), WebType VARCHAR(100), CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), unique(ValueType,WebType,LocalValue,RemoteValue) )
```

### `metadata` (2500 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `DataID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `DBId` | `INTEGER` | no | no | `` | 0 | 0 |
| `Title` | `TEXT` | no | no | `` | 4 | 1081 |
| `Size` | `INTEGER` | no | no | `0` | 0 | 0 |
| `Path` | `TEXT` | no | no | `` | 0 | 0 |
| `Hash` | `VARCHAR(32)` | no | no | `` | 0 | 1393 |
| `Country` | `VARCHAR(50)` | no | no | `` | 4 | 2496 |
| `ReleaseDate` | `VARCHAR(30)` | no | no | `` | 4 | 1282 |
| `ReleaseYear` | `INT` | no | no | `1900` | 0 | 0 |
| `ViewCount` | `INT` | no | no | `0` | 0 | 0 |
| `DataType` | `INT` | no | no | `0` | 0 | 0 |
| `Rating` | `FLOAT` | no | no | `0.0` | 0 | 0 |
| `RatingCount` | `INT` | no | no | `0` | 0 | 0 |
| `FavoriteCount` | `INT` | no | no | `0` | 0 | 0 |
| `Genre` | `TEXT` | no | no | `` | 4 | 1276 |
| `Grade` | `FLOAT` | no | no | `0.0` | 0 | 0 |
| `ViewDate` | `VARCHAR(30)` | no | no | `` | 3 | 1921 |
| `FirstScanDate` | `VARCHAR(30)` | no | no | `` | 0 | 0 |
| `LastScanDate` | `VARCHAR(30)` | no | no | `` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `PathExist` | `INT` | no | no | `0` | 0 | 0 |

Foreign keys: none declared.

- Index `metadata_idx_DBId_DataType_Title` (DBId, DataType, Title), unique: False
- Index `metadata_idx_DBId_DataType_PathExist` (DBId, DataType, PathExist), unique: False
- Index `metadata_idx_DBId_DataType_ViewCount` (DBId, DataType, ViewCount), unique: False
- Index `metadata_idx_DBId_DataType_ViewDate` (DBId, DataType, ViewDate), unique: False
- Index `metadata_idx_DBId_DataType_Size` (DBId, DataType, Size), unique: False
- Index `metadata_idx_DBId_DataType_Grade` (DBId, DataType, Grade), unique: False
- Index `metadata_idx_DBId_DataType_LastScanDate` (DBId, DataType, LastScanDate), unique: False
- Index `metadata_idx_DBId_DataType_FirstScanDate` (DBId, DataType, FirstScanDate), unique: False
- Index `metadata_idx_DBId_DataType_ReleaseDate` (DBId, DataType, ReleaseDate), unique: False
- Index `metadata_idx_DBId_DataType_Hash` (DBId, DataType, Hash), unique: False
- Index `metadata_idx_DBId_Hash` (DBId, Hash), unique: False
- Index `metadata_idx_Hash` (Hash), unique: False
- Index `metadata_idx_DBId_DataID` (DBId, DataType), unique: False
- Index `metadata_idx_DataID` (DataID), unique: False

```sql
CREATE TABLE metadata ( DataID INTEGER PRIMARY KEY autoincrement, DBId INTEGER, Title TEXT, Size  INTEGER DEFAULT 0, Path TEXT, Hash VARCHAR(32), Country VARCHAR(50), ReleaseDate VARCHAR(30), ReleaseYear INT DEFAULT 1900, ViewCount INT DEFAULT 0, DataType INT DEFAULT 0, Rating FLOAT DEFAULT 0.0, RatingCount INT DEFAULT 0, FavoriteCount INT DEFAULT 0, Genre TEXT, Grade FLOAT DEFAULT 0.0, ViewDate VARCHAR(30), FirstScanDate VARCHAR(30), LastScanDate VARCHAR(30), CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')) , PathExist INT DEFAULT 0)
```

### `metadata_comic` (0 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `CID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `DataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `Language` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `ComicType` | `INT` | no | no | `0` | 0 | 0 |
| `Artist` | `TEXT` | no | no | `` | 0 | 0 |
| `Plot` | `TEXT` | no | no | `` | 0 | 0 |
| `Outline` | `TEXT` | no | no | `` | 0 | 0 |
| `PicCount` | `INTEGER` | no | no | `0` | 0 | 0 |
| `PicPaths` | `TEXT` | no | no | `` | 0 | 0 |
| `WebType` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `WebUrl` | `VARCHAR(2000)` | no | no | `` | 0 | 0 |
| `ExtraInfo` | `TEXT` | no | no | `` | 0 | 0 |

Foreign keys: none declared.

- Index `metadata_comic_idx_DataID_CID` (DataID, CID), unique: False
- Index `sqlite_autoindex_metadata_comic_1` (DataID, CID), unique: True

```sql
CREATE TABLE metadata_comic( CID INTEGER PRIMARY KEY autoincrement, DataID INTEGER, Language VARCHAR(100), ComicType INT DEFAULT 0, Artist TEXT, Plot TEXT, Outline TEXT, PicCount INTEGER DEFAULT 0, PicPaths TEXT, WebType  VARCHAR(100), WebUrl  VARCHAR(2000), ExtraInfo TEXT, unique(DataID,CID) )
```

### `metadata_game` (0 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `GID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `DataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `Branch` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `OriginalPainting` | `VARCHAR(200)` | no | no | `` | 0 | 0 |
| `VoiceActors` | `VARCHAR(200)` | no | no | `` | 0 | 0 |
| `Play` | `VARCHAR(200)` | no | no | `` | 0 | 0 |
| `Music` | `VARCHAR(200)` | no | no | `` | 0 | 0 |
| `Singers` | `VARCHAR(200)` | no | no | `` | 0 | 0 |
| `Plot` | `TEXT` | no | no | `` | 0 | 0 |
| `Outline` | `TEXT` | no | no | `` | 0 | 0 |
| `ExtraName` | `TEXT` | no | no | `` | 0 | 0 |
| `Studio` | `TEXT` | no | no | `` | 0 | 0 |
| `Publisher` | `TEXT` | no | no | `` | 0 | 0 |
| `WebType` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `WebUrl` | `VARCHAR(2000)` | no | no | `` | 0 | 0 |
| `ExtraInfo` | `TEXT` | no | no | `` | 0 | 0 |

Foreign keys: none declared.

- Index `metadata_game_idx_DataID_GID` (DataID, GID), unique: False
- Index `sqlite_autoindex_metadata_game_1` (DataID, GID), unique: True

```sql
CREATE TABLE metadata_game( GID INTEGER PRIMARY KEY autoincrement, DataID INTEGER, Branch VARCHAR(100), OriginalPainting VARCHAR(200), VoiceActors VARCHAR(200), Play VARCHAR(200), Music VARCHAR(200), Singers VARCHAR(200), Plot TEXT, Outline TEXT, ExtraName TEXT, Studio TEXT, Publisher TEXT, WebType  VARCHAR(100), WebUrl  VARCHAR(2000), ExtraInfo TEXT, unique(DataID,GID) )
```

### `metadata_picture` (0 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `PID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `DataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `Director` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `Studio` | `TEXT` | no | no | `` | 0 | 0 |
| `Publisher` | `TEXT` | no | no | `` | 0 | 0 |
| `Plot` | `TEXT` | no | no | `` | 0 | 0 |
| `Outline` | `TEXT` | no | no | `` | 0 | 0 |
| `PicCount` | `INTEGER` | no | no | `0` | 0 | 0 |
| `PicPaths` | `TEXT` | no | no | `` | 0 | 0 |
| `VideoPaths` | `TEXT` | no | no | `` | 0 | 0 |
| `ExtraInfo` | `TEXT` | no | no | `` | 0 | 0 |

Foreign keys: none declared.

- Index `metadata_picture_idx_DataID_PID` (DataID, PID), unique: False
- Index `sqlite_autoindex_metadata_picture_1` (DataID, PID), unique: True

```sql
CREATE TABLE metadata_picture( PID INTEGER PRIMARY KEY autoincrement, DataID INTEGER, Director VARCHAR(100), Studio TEXT, Publisher TEXT, Plot TEXT, Outline TEXT, PicCount INTEGER DEFAULT 0, PicPaths TEXT, VideoPaths TEXT, ExtraInfo TEXT, unique(DataID,PID) )
```

### `metadata_to_actor` (1572 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `ID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `ActorID` | `INTEGER` | no | no | `` | 0 | 0 |
| `DataID` | `INT` | no | no | `` | 0 | 0 |

Foreign keys: none declared.

- Index `metadata_to_actor_idx_DataID_ActorID` (DataID, ActorID), unique: False
- Index `metadata_to_actor_idx_ActorID_DataID` (ActorID, DataID), unique: False
- Index `metadata_to_actor_idx_DataID` (DataID, ActorID), unique: False
- Index `metadata_to_actor_idx_ActorID` (ActorID, DataID), unique: False
- Index `sqlite_autoindex_metadata_to_actor_1` (ActorID, DataID), unique: True

```sql
CREATE TABLE metadata_to_actor( ID INTEGER PRIMARY KEY autoincrement, ActorID INTEGER, DataID INT, unique(ActorID,DataID) )
```

### `metadata_to_label` (1372 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `id` | `INTEGER` | yes | no | `` | 0 | 0 |
| `DataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `LabelName` | `VARCHAR(200)` | no | no | `` | 0 | 0 |

Foreign keys: none declared.

- Index `metadata_to_label_idx_LabelName` (LabelName), unique: False
- Index `metadata_to_label_idx_DataID` (DataID), unique: False
- Index `sqlite_autoindex_metadata_to_label_1` (DataID, LabelName), unique: True

```sql
CREATE TABLE metadata_to_label( id INTEGER PRIMARY KEY autoincrement, DataID INTEGER, LabelName VARCHAR(200), unique(DataID,LabelName) )
```

### `metadata_to_tagstamp` (2343 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `id` | `INTEGER` | yes | no | `` | 0 | 0 |
| `DataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `TagID` | `INTEGER` | no | no | `` | 0 | 0 |

Foreign keys: none declared.

- Index `metadata_to_tagstamp_idx_TagID` (TagID), unique: False
- Index `metadata_to_tagstamp_idx_DataID` (DataID), unique: False
- Index `sqlite_autoindex_metadata_to_tagstamp_1` (DataID, TagID), unique: True

```sql
CREATE TABLE metadata_to_tagstamp( id INTEGER PRIMARY KEY autoincrement, DataID INTEGER, TagID INTEGER, unique(DataID,TagID) )
```

### `metadata_to_translation` (0 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `id` | `INTEGER` | yes | no | `` | 0 | 0 |
| `DataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `FieldType` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `TransaltionID` | `INTEGER` | no | no | `` | 0 | 0 |

Foreign keys: none declared.

- Index `metadata_to_translation_idx_DataID_FieldType` (DataID, FieldType), unique: False
- Index `sqlite_autoindex_metadata_to_translation_1` (DataID, FieldType, TransaltionID), unique: True

```sql
CREATE TABLE metadata_to_translation( id INTEGER PRIMARY KEY autoincrement, DataID INTEGER, FieldType VARCHAR(100), TransaltionID INTEGER, unique(DataID,FieldType,TransaltionID) )
```

### `metadata_video` (2500 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `MVID` | `INTEGER` | yes | no | `` | 0 | 0 |
| `DataID` | `INTEGER` | no | no | `` | 0 | 0 |
| `VID` | `VARCHAR(500)` | no | no | `` | 0 | 1078 |
| `VideoType` | `INT` | no | no | `0` | 0 | 0 |
| `Series` | `TEXT` | no | no | `` | 0 | 1762 |
| `Director` | `VARCHAR(100)` | no | no | `` | 0 | 1881 |
| `Studio` | `TEXT` | no | no | `` | 0 | 1297 |
| `Publisher` | `TEXT` | no | no | `` | 0 | 2070 |
| `Plot` | `TEXT` | no | no | `` | 0 | 1711 |
| `Outline` | `TEXT` | no | no | `` | 0 | 2140 |
| `Duration` | `INT` | no | no | `0` | 0 | 0 |
| `SubSection` | `TEXT` | no | no | `` | 0 | 2447 |
| `ImageUrls` | `TEXT` | no | no | `''` | 0 | 1082 |
| `WebType` | `VARCHAR(100)` | no | no | `` | 0 | 1082 |
| `WebUrl` | `VARCHAR(2000)` | no | no | `` | 0 | 1081 |
| `ExtraInfo` | `TEXT` | no | no | `` | 0 | 2500 |

Foreign keys: none declared.

- Index `metadata_video_idx_DataID` (DataID), unique: False
- Index `metadata_video_idx_VideoType` (VideoType), unique: False
- Index `metadata_video_idx_VID` (VID), unique: False
- Index `metadata_video_idx_DataID_VID` (DataID, VID), unique: False
- Index `sqlite_autoindex_metadata_video_1` (DataID, VID), unique: True

```sql
CREATE TABLE metadata_video( MVID INTEGER PRIMARY KEY autoincrement, DataID INTEGER, VID VARCHAR(500), VideoType INT DEFAULT 0, Series TEXT, Director VARCHAR(100), Studio TEXT, Publisher TEXT, Plot TEXT, Outline TEXT, Duration INT DEFAULT 0, SubSection TEXT, ImageUrls TEXT DEFAULT '', WebType  VARCHAR(100), WebUrl  VARCHAR(2000), ExtraInfo TEXT, unique(DataID,VID) )
```

## configuration database

- Path: `D:\Jvedio\Jvedio5.0\data\Administrator\app_configs.sqlite`
- SHA-256: `f746898825504d4b0d0e92f00003aec9aef5231ff64236eb7e71efbbf4072b6a`
- Size: 57344 bytes
- Tables: 1

### `app_configs` (19 rows)

| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |
|---|---|---:|---:|---|---:|---:|
| `ConfigId` | `INTEGER` | yes | no | `` | 0 | 0 |
| `ConfigName` | `VARCHAR(100)` | no | no | `` | 0 | 0 |
| `ConfigValue` | `TEXT` | no | no | `''` | 0 | 0 |
| `CreateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |
| `UpdateDate` | `VARCHAR(30)` | no | no | `STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')` | 0 | 0 |

Foreign keys: none declared.

- Index `app_configs_idx_ConfigName` (ConfigName), unique: False
- Index `sqlite_autoindex_app_configs_1` (ConfigName), unique: True

```sql
CREATE TABLE app_configs ( ConfigId INTEGER PRIMARY KEY autoincrement, ConfigName VARCHAR(100), ConfigValue TEXT DEFAULT '', CreateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), UpdateDate VARCHAR(30) DEFAULT(STRFTIME('%Y-%m-%d %H:%M:%S', 'NOW', 'localtime')), unique(ConfigName) )
```
