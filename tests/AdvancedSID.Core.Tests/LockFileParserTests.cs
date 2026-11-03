using System.Text;
using AdvancedSID.Core;
using Xunit;

namespace AdvancedSID.Core.Tests;

public class LockFileParserTests
{
    // The sample from a real FSLogix lock file, as it shows in Notepad.
    private const string Sample = "    堬ꃰơ  DCLAVDPRDSH-2.dcl.co.uk" + "                                                                ";

    [Fact]
    public void Extracts_server_from_real_world_sample()
    {
        var bytes = Encoding.Unicode.GetBytes(Sample);
        var hosts = LockFileParser.ExtractHostNames(bytes);
        Assert.Equal("DCLAVDPRDSH-2.dcl.co.uk", hosts[0]);
    }

    [Fact]
    public void Extracts_server_with_binary_header()
    {
        var header = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x2C, 0x58, 0xF0, 0xA0, 0xA1, 0x01, 0x00, 0x00, 0x20, 0x00, 0x20, 0x00 };
        var bytes = header.Concat(Encoding.Unicode.GetBytes("AVD-HOST-07.contoso.local")).Concat(new byte[512]).ToArray();
        Assert.Equal("AVD-HOST-07.contoso.local", LockFileParser.ExtractHostNames(bytes)[0]);
    }

    [Fact]
    public void Extracts_short_netbios_name()
    {
        var bytes = Encoding.Unicode.GetBytes("\u0001\u0002  RDSH01   ");
        Assert.Equal("RDSH01", LockFileParser.ExtractHostNames(bytes)[0]);
    }

    [Fact]
    public void Read_returns_server_and_short_name()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Encoding.Unicode.GetBytes(Sample));
            var details = LockFileParser.Read(path);
            Assert.Null(details.ReadError);
            Assert.Equal("DCLAVDPRDSH-2.dcl.co.uk", details.Server);
            Assert.Equal("DCLAVDPRDSH-2", details.ShortServer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_reports_error_for_missing_file()
    {
        var details = LockFileParser.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lock"));
        Assert.NotNull(details.ReadError);
        Assert.Null(details.Server);
    }
}
