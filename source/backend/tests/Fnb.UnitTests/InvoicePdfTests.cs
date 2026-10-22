using Fnb.Application.Abstractions;
using Fnb.Infrastructure.Pdf;

namespace Fnb.UnitTests;

public sealed class InvoicePdfTests
{
    private static InvoicePdfModel Model() => new(
        "Demo Coffee", "Chi nhánh Quận 1", "Địa chỉ demo", "0000000001", "HCM-Q1-20260301-0001", 12,
        new DateTime(2026, 3, 1, 9, 30, 0), "Bàn 3",
        [
            new InvoicePdfLine("Cà phê sữa đá", "L", "Thêm shot espresso", "Ít đường", 2, 46000, 92000),
            new InvoicePdfLine("Trà đào cam sả", "M", null, null, 1, 42000, 42000),
        ],
        134000, 0, 0, 0, true, 0, 134000, "Tiền mặt", "Đã thanh toán", "Cảm ơn quý khách!", null);

    [Theory]
    [InlineData(InvoicePaperSize.K80)]
    [InlineData(InvoicePaperSize.A5)]
    public void Renders_valid_pdf_with_vietnamese_text(InvoicePaperSize size)
    {
        var bytes = new QuestPdfInvoiceRenderer().Render(Model(), size);
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void Money_format_uses_vietnamese_grouping()
    {
        Assert.Equal("134.000 ₫", QuestPdfInvoiceRenderer.Money(134000));
    }
}
