﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlatSharp;
using LeagueToolkit.Helpers.Exceptions;
using ZstdSharp;

namespace LeagueToolkit.IO.ReleaseManifestFile;

public class ReleaseManifest
{
    private readonly ReleaseManifestBody _body;

    public ReleaseManifest(string fileLocation) : this(File.OpenRead(fileLocation))
    {
    }

    public ReleaseManifest(Stream stream)
    {
        using (var br = new BinaryReader(stream))
        {
            var magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (magic != "RMAN") throw new InvalidFileSignatureException();

            var major = br.ReadByte();
            var minor = br.ReadByte();
            // NOTE: only check major because minor version are compatabile forwards-backwards
            if (major != 2) throw new UnsupportedFileVersionException();

            //Could possibly be Compression Type
            var unknown = br.ReadByte();
            if (unknown != 0) throw new Exception("Unknown: " + unknown);

            var signatureType = br.ReadByte();
            var contentOffset = br.ReadUInt32();
            var compressedContentSize = br.ReadUInt32();
            ID = br.ReadUInt64();
            var uncompressedContentSize = br.ReadUInt32();

            br.BaseStream.Seek(contentOffset, SeekOrigin.Begin);
            var compressedFile = br.ReadBytes((int) compressedContentSize);

            if (signatureType != 0)
            {
                var signature = br.ReadBytes(256);
                // NOTE: verify signature here
            }

            var uncompressedFile = Zstd.Decompress(compressedFile, (int) uncompressedContentSize);
            _body = FlatBufferSerializer.Default.Parse<ReleaseManifestBody>(uncompressedFile);
        }
    }

    public ulong ID { get; }
    public IList<ReleaseManifestBundle> Bundles => _body.Bundles;
    public IList<ReleaseManifestLanguage> Languages => _body.Languages;
    public IList<ReleaseManifestFile> Files => _body.Files;
    public IList<ReleaseManifestDirectory> Directories => _body.Directories;
    public IList<ReleaseManifestEncryptionKey> EncryptionKeys => _body.EncryptionKeys;
    public IList<ReleaseManifestChunkingParameter> ChunkingParameters => _body.ChunkingParameters;

    public void Write(string fileLocation)
    {
        Write(File.Create(fileLocation));
    }

    public void Write(Stream stream, bool leaveOpen = false)
    {
        var magic = Encoding.ASCII.GetBytes("RMAN");
        byte major = 2;
        byte minor = 0;
        byte unknown = 0;
        byte signatureType = 0;
        var contentOffset = 4 + 4 + 4 + 4 + 8 + 4;

        var uncompressedFile = new byte[FlatBufferSerializer.Default.GetMaxSize(_body)];
        var uncompressedContentSize = FlatBufferSerializer.Default.Serialize(_body, uncompressedFile);
        Array.Resize(ref uncompressedFile, uncompressedContentSize);

        var compressedFile = Zstd.Compress(uncompressedFile);
        var compressedContentSize = compressedFile.Length;

        using (var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen))
        {
            bw.Write(magic);
            bw.Write(major);
            bw.Write(minor);
            bw.Write(unknown);
            bw.Write(signatureType);
            bw.Write(contentOffset);
            bw.Write(compressedContentSize);
            bw.Write(ID);
            bw.Write(uncompressedContentSize);
            bw.Write(compressedFile);
        }
    }
}