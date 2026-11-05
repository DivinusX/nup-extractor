using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using NupExtractorApp;
using Xunit;
using ZstdSharp;

namespace NupExtractorApp.Tests;

public class NupTests
{
    private static readonly string SampleNupPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../sample_game.nup"));

    [Fact]
    public void Test_Superblock_Parsing_ValidContainer()
    {
        Assert.True(File.Exists(SampleNupPath), $"Sample NUP not found at {SampleNupPath}");
        using var reader = new NupReader(SampleNupPath);

        Assert.Equal(NupConstants.HeaderSize, reader.Super.HeaderSize);
        Assert.Equal(1, reader.Super.HeaderVersion);
        Assert.True(reader.Super.FileSize > 0);
        Assert.Equal((ulong)new FileInfo(SampleNupPath).Length, reader.Super.FileSize);
        Assert.Equal(2u, reader.Super.PackageCount);
        Assert.False(string.IsNullOrEmpty(reader.Super.FormattedUuid));
    }

    [Fact]
    public void Test_Superblock_Validation_RejectsBadMagic()
    {
        byte[] fakeData = new byte[NupConstants.HeaderSize];
        Encoding.ASCII.GetBytes("BAD!").CopyTo(fakeData, 0);

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, fakeData);
            Assert.Throws<InvalidDataException>(() => new NupReader(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test_TOC_Parsing_PackagesAndNodes()
    {
        using var reader = new NupReader(SampleNupPath);

        Assert.Equal(2, reader.Packages.Count);
        var basePkg = reader.Packages[0];
        Assert.Equal(NupConstants.KindBase, basePkg.Kind);
        Assert.Equal("base", basePkg.KindName);
        Assert.Equal("0100123456789ABC", basePkg.TitleIdHex);

        var updatePkg = reader.Packages[1];
        Assert.Equal(NupConstants.KindUpdate, updatePkg.Kind);
        Assert.Equal("update", updatePkg.KindName);

        Assert.True(reader.Dirs.Count > 0);
        Assert.True(reader.Files.Count > 0);
        Assert.True(reader.Children.Count > 0);
    }

    [Fact]
    public void Test_Metadata_Extraction()
    {
        using var reader = new NupReader(SampleNupPath);
        var basePkg = reader.Packages[0];
        var meta = reader.GetPackageMetadata(basePkg);

        Assert.True(meta.ContainsKey(NupConstants.TlvTitleName));
        string title = Encoding.UTF8.GetString(meta[NupConstants.TlvTitleName]);
        Assert.Equal("Sample NX Adventure", title);

        Assert.True(meta.ContainsKey(NupConstants.TlvPublisher));
        string publisher = Encoding.UTF8.GetString(meta[NupConstants.TlvPublisher]);
        Assert.Equal("Nintendo", publisher);

        Assert.True(meta.ContainsKey(NupConstants.TlvDisplayVersion));
        string version = Encoding.UTF8.GetString(meta[NupConstants.TlvDisplayVersion]);
        Assert.Equal("1.1.0", version);

        Assert.True(meta.ContainsKey(NupConstants.TlvIcon));
        Assert.True(meta[NupConstants.TlvIcon].Length > 0);
    }

    [Fact]
    public void Test_Inspect_ReturnsCompleteContract()
    {
        var data = NupEngine.Inspect(SampleNupPath);

        Assert.NotNull(data);
        Assert.Equal("sample_game.nup", data["file_name"]);
        Assert.True(data.ContainsKey("primary_meta"));
        Assert.True(data.ContainsKey("packages"));
        Assert.True(data.ContainsKey("stats"));

        var packages = (List<Dictionary<string, object>>)data["packages"];
        Assert.Equal(2, packages.Count);

        var primaryMeta = (Dictionary<string, object>)data["primary_meta"];
        Assert.Equal("Sample NX Adventure", primaryMeta["title_name"]);
        Assert.Equal("Nintendo", primaryMeta["publisher"]);
    }

    [Fact]
    public void Test_Tree_Children_Resolution()
    {
        using var reader = new NupReader(SampleNupPath);
        var basePkg = reader.Packages[0];

        var tree = NupEngine.GetDirChildren(SampleNupPath, basePkg.RootDir);
        Assert.NotNull(tree);
        var children = (List<Dictionary<string, object>>)tree["children"];
        Assert.True(children.Count > 0);

        bool foundExeFs = false;
        bool foundRomFs = false;
        foreach (var c in children)
        {
            if (c["name"].ToString() == "exefs") foundExeFs = true;
            if (c["name"].ToString() == "romfs") foundRomFs = true;
        }

        Assert.True(foundExeFs, "exefs not found in root");
        Assert.True(foundRomFs, "romfs not found in root");
    }

    [Fact]
    public void Test_File_Search()
    {
        var results = NupEngine.SearchFiles(SampleNupPath, "level");
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r["name"].ToString() == "level01.dat");

        var noResults = NupEngine.SearchFiles(SampleNupPath, "non_existent_file_name_12345");
        Assert.Empty(noResults);
    }

    [Fact]
    public void Test_Decompression_RawAndZstd()
    {
        using var reader = new NupReader(SampleNupPath);

        int rawFileIndex = -1;
        int zstdFileIndex = -1;

        for (int i = 0; i < reader.Files.Count; i++)
        {
            if (reader.Files[i].Codec == NupConstants.CodecRaw && rawFileIndex < 0)
                rawFileIndex = i;
            if (reader.Files[i].Codec == NupConstants.CodecZstd && zstdFileIndex < 0)
                zstdFileIndex = i;
        }

        Assert.True(rawFileIndex >= 0, "No RAW file found in sample container");
        Assert.True(zstdFileIndex >= 0, "No ZSTD file found in sample container");

        byte[] rawData = reader.ReadFileData(rawFileIndex);
        Assert.Equal((ulong)rawData.Length, reader.Files[rawFileIndex].Size);
        byte[] rawHash = SHA256.HashData(rawData);
        Assert.Equal(rawHash, reader.Files[rawFileIndex].Sha256);

        byte[] zstdData = reader.ReadFileData(zstdFileIndex);
        Assert.Equal((ulong)zstdData.Length, reader.Files[zstdFileIndex].Size);
        byte[] zstdHash = SHA256.HashData(zstdData);
        Assert.Equal(zstdHash, reader.Files[zstdFileIndex].Sha256);
    }

    [Fact]
    public void Test_Full_Extraction_AllPackages()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "nup_test_extract_" + Guid.NewGuid().ToString("N"));
        try
        {
            var progressUpdates = new List<ProgressInfo>();
            var progress = new Progress<ProgressInfo>(info => progressUpdates.Add(info));

            NupEngine.Extract(SampleNupPath, outDir, "all", null, null, progress, CancellationToken.None);

            Assert.True(Directory.Exists(outDir));
            Assert.True(File.Exists(Path.Combine(outDir, "base", "exefs", "main")));
            Assert.True(File.Exists(Path.Combine(outDir, "base", "romfs", "data", "config.json")));
            Assert.True(File.Exists(Path.Combine(outDir, "base", "romfs", "data", "levels", "level01.dat")));
            Assert.True(File.Exists(Path.Combine(outDir, "base", "romfs", "textures", "ui_sheet.bin")));
            Assert.True(File.Exists(Path.Combine(outDir, "updates", "65536", "romfs", "data", "patch_notes.txt")));

            Assert.Equal(2144, new FileInfo(Path.Combine(outDir, "base", "exefs", "main")).Length);
            Assert.Equal(92, new FileInfo(Path.Combine(outDir, "base", "romfs", "data", "config.json")).Length);
            Assert.Equal(8000, new FileInfo(Path.Combine(outDir, "base", "romfs", "data", "levels", "level01.dat")).Length);
            Assert.Equal(13000, new FileInfo(Path.Combine(outDir, "base", "romfs", "textures", "ui_sheet.bin")).Length);
            Assert.Equal(77, new FileInfo(Path.Combine(outDir, "updates", "65536", "romfs", "data", "patch_notes.txt")).Length);
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public void Test_Package_Mode_Extraction()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "nup_test_pkg_" + Guid.NewGuid().ToString("N"));
        try
        {
            var progress = new Progress<ProgressInfo>(_ => { });
            NupEngine.Extract(SampleNupPath, outDir, "package", 0, null, progress, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(outDir, "exefs", "main")));
            Assert.True(File.Exists(Path.Combine(outDir, "romfs", "data", "config.json")));
            Assert.False(File.Exists(Path.Combine(outDir, "updates", "65536", "romfs", "data", "patch_notes.txt")));
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public void Test_Subtree_Mode_Extraction()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "nup_test_subtree_" + Guid.NewGuid().ToString("N"));
        try
        {
            var progress = new Progress<ProgressInfo>(_ => { });
            NupEngine.Extract(SampleNupPath, outDir, "subtree", 0, "romfs/data", progress, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(outDir, "config.json")));
            Assert.True(File.Exists(Path.Combine(outDir, "levels", "level01.dat")));
            Assert.False(File.Exists(Path.Combine(outDir, "main")));
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public void Test_Integrity_Verification()
    {
        VerifyProgressInfo? finalResult = null;
        var progress = new SyncProgress<VerifyProgressInfo>(p =>
        {
            if (p.Status == "done") finalResult = p;
        });

        NupEngine.Verify(SampleNupPath, deep: true, progress, CancellationToken.None);

        Assert.NotNull(finalResult);
        Assert.True(finalResult.Valid);
        Assert.Empty(finalResult.Problems);
        Assert.Equal(5, finalResult.TotalFilesChecked);
    }

    [Fact]
    public void Test_Serialization_Contract_ProducesCamelCase()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var info = new ProgressInfo
        {
            Status = "extracting",
            Current = 5,
            Total = 10,
            Percent = 50.0,
            FileName = "romfs/test.dat",
            BytesWritten = 1024,
            TotalBytes = 2048,
            DestDir = "C:\\test"
        };

        string json = JsonSerializer.Serialize(new
        {
            type = "extract_progress",
            info
        }, options);

        // Verify keys are camelCase and NOT PascalCase
        Assert.Contains("\"percent\":50", json);
        Assert.Contains("\"current\":5", json);
        Assert.Contains("\"total\":10", json);
        Assert.Contains("\"fileName\":\"romfs/test.dat\"", json);
        Assert.Contains("\"bytesWritten\":1024", json);
        Assert.Contains("\"totalBytes\":2048", json);
        Assert.Contains("\"destDir\":", json);

        // Verify deserializer reads without error
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var infoElem = root.GetProperty("info");
        Assert.Equal(50.0, infoElem.GetProperty("percent").GetDouble());
    }

    [Fact]
    public void Test_Extraction_ThrowsOnEmptyPath()
    {
        var progress = new Progress<ProgressInfo>(_ => { });
        Assert.Throws<ArgumentException>(() => NupEngine.Extract("", "some_dest", "all", null, null, progress, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => NupEngine.Extract(SampleNupPath, "", "all", null, null, progress, CancellationToken.None));
    }

    [Fact]
    public void Test_GetDirChildren_OutOfBoundsThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NupEngine.GetDirChildren(SampleNupPath, 999999));
    }

    [Fact]
    public void Test_OpenReadFileStream_StreamingDecompression()
    {
        using var reader = new NupReader(SampleNupPath);
        Assert.True(reader.Files.Count > 0);

        for (int i = 0; i < reader.Files.Count; i++)
        {
            var f = reader.Files[i];
            using var stream = reader.OpenReadFileStream(i);
            Assert.NotNull(stream);

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Assert.Equal((long)f.Size, ms.Length);

            byte[] hash = SHA256.HashData(ms.ToArray());
            Assert.Equal(Convert.ToHexString(f.Sha256), Convert.ToHexString(hash));
        }
    }

    [Fact]
    public void Test_DeepVerify_UsesStreamingWithoutMemorySpikes()
    {
        VerifyProgressInfo? finalResult = null;
        var syncProgress = new SyncProgress<VerifyProgressInfo>(p =>
        {
            if (p.Status == "done") finalResult = p;
        });

        NupEngine.Verify(SampleNupPath, deep: true, syncProgress, CancellationToken.None);

        Assert.NotNull(finalResult);
        Assert.True(finalResult.Valid);
        Assert.Empty(finalResult.Problems);
    }

    private class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
