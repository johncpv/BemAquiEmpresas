using System.Text;
using BemAqui.BuildingBlocks.Attachments;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>FND-07 CA 18: extensão e magic bytes precisam concordar com a allowlist.</summary>
public class AttachmentPolicyTests
{
    private static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);

    public static TheoryData<string, byte[], string> Accepted => new()
    {
        { "proposta.pdf", Text("%PDF-1.7\n..."), "application/pdf" },
        { "foto.PNG", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00], "image/png" },
        { "foto.jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0x00], "image/jpeg" },
        { "nfe.xml", Text("﻿<?xml version=\"1.0\"?><nfeProc/>"), "application/xml" },
        { "orcamento.xlsx", [0x50, 0x4B, 0x03, 0x04, 0x14], "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { "itens.csv", Text("codigo;descricao\n001;Motor"), "text/csv" },
        { "cabine.dwg", Text("AC1032\0\0"), "application/acad" },
        { "cabine.dxf", Text("  0\r\nSECTION\r\n"), "application/dxf" },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void Tipo_permitido_com_conteudo_coerente_e_aceito(string fileName, byte[] head, string expectedContentType) =>
        AttachmentPolicy.Match(fileName, head)!.ContentType.Should().Be(expectedContentType);

    public static TheoryData<string, byte[]> Rejected => new()
    {
        { "virus.exe", [0x4D, 0x5A, 0x90, 0x00] },
        { "fatura.pdf", [0x4D, 0x5A, 0x90, 0x00] },
        { "logo.svg", Text("<svg xmlns=\"http://www.w3.org/2000/svg\"/>") },
        { "pagina.html", Text("<!doctype html><html></html>") },
        { "nota.xml", Text("<html><script>alert(1)</script></html>") },
        { "lista.csv", Text("<script>fetch('/api')</script>") },
        { "foto.png", Text("%PDF-1.7") },
        { "sem-extensao", Text("%PDF-1.7") },
        { "binario.csv", [0x41, 0x00, 0x42] },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Fora_da_allowlist_ou_com_conteudo_divergente_e_recusado(string fileName, byte[] head) =>
        AttachmentPolicy.Match(fileName, head).Should().BeNull();

    [Theory]
    [InlineData("stream: OK", ScanVerdict.Clean, null)]
    [InlineData("stream: Eicar-Signature FOUND", ScanVerdict.Infected, "Eicar-Signature")]
    [InlineData("INSTREAM size limit exceeded. ERROR", ScanVerdict.TooLarge, null)]
    [InlineData("", ScanVerdict.Unavailable, null)]
    public void Resposta_do_clamd_e_interpretada(string reply, ScanVerdict verdict, string? signature)
    {
        var result = ClamAvScanner.Parse(reply);

        result.Verdict.Should().Be(verdict);
        result.Signature.Should().Be(signature);
    }
}
