using System.Globalization;
using Fnb.Application.Abstractions;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Fnb.Infrastructure.Pdf;

/// <summary>
/// Internal invoice / receipt PDF (NOT a legal e-invoice). K80 = 80 mm thermal roll with continuous height; A5 = sheet.
/// Uses the embedded Be Vietnam Pro font (SIL OFL 1.1) so Vietnamese diacritics render identically on every host.
/// QuestPDF Community license: free for organizations under the revenue threshold — review before commercial use.
/// </summary>
public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    public const string FontFamily = "Be Vietnam Pro";
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly Lock InitLock = new();
    private static bool _initialized;

    public QuestPdfInvoiceRenderer() => EnsureInitialized();

    private static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseSystemFonts = false;
            var assembly = typeof(QuestPdfInvoiceRenderer).Assembly;
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Embedded font {name} missing.");
                // Family name stored in the font file is "Be Vietnam Pro".
                FontManager.RegisterFontFromStream(stream);
            }

            _initialized = true;
        }
    }

    public static string Money(decimal value) => value.ToString("#,##0", Vi) + " ₫";

    public byte[] Render(InvoicePdfModel model, InvoicePaperSize size)
    {
        var k80 = size == InvoicePaperSize.K80;
        var baseFont = k80 ? 8f : 10f;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                if (k80)
                {
                    page.ContinuousSize(80, Unit.Millimetre);
                    page.Margin(4, Unit.Millimetre);
                }
                else
                {
                    page.Size(PageSizes.A5);
                    page.Margin(12, Unit.Millimetre);
                }

                page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(baseFont).FontColor(Colors.Black));
                page.Content().Column(col =>
                {
                    col.Spacing(k80 ? 2 : 4);
                    col.Item().AlignCenter().Text(model.BrandName).Bold().FontSize(baseFont + 4);
                    col.Item().AlignCenter().Text(model.BranchName).SemiBold();
                    col.Item().AlignCenter().Text(model.BranchAddress).FontSize(baseFont - 1);
                    if (!string.IsNullOrWhiteSpace(model.BranchPhone))
                    {
                        col.Item().AlignCenter().Text($"ĐT: {model.BranchPhone}").FontSize(baseFont - 1);
                    }

                    col.Item().PaddingVertical(3).LineHorizontal(0.5f);
                    col.Item().AlignCenter().Text("HÓA ĐƠN BÁN HÀNG").Bold().FontSize(baseFont + 2);
                    col.Item().AlignCenter().Text("(Hóa đơn nội bộ – không phải hóa đơn điện tử)").Italic().FontSize(baseFont - 2);
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text($"Số HĐ: {model.InvoiceNo}");
                        r.RelativeItem().AlignRight().Text($"Đơn #{model.OrderNumber}").Bold();
                    });
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text(model.IssuedAtLocal.ToString("dd/MM/yyyy HH:mm", Vi));
                        if (!string.IsNullOrWhiteSpace(model.TableName))
                        {
                            r.RelativeItem().AlignRight().Text($"Bàn: {model.TableName}");
                        }
                    });
                    col.Item().PaddingVertical(3).LineHorizontal(0.5f);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(k80 ? 5 : 6);
                            c.ConstantColumn(k80 ? 18 : 30);
                            if (!k80)
                            {
                                c.RelativeColumn(2);
                            }

                            c.RelativeColumn(k80 ? 3 : 2);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Món").Bold();
                            h.Cell().AlignCenter().Text("SL").Bold();
                            if (!k80)
                            {
                                h.Cell().AlignRight().Text("Đơn giá").Bold();
                            }

                            h.Cell().AlignRight().Text("T.Tiền").Bold();
                        });
                        foreach (var line in model.Lines)
                        {
                            table.Cell().PaddingTop(2).Column(c =>
                            {
                                c.Item().Text($"{line.Name} ({line.Variant})");
                                if (!string.IsNullOrWhiteSpace(line.AddOns))
                                {
                                    c.Item().Text($"+ {line.AddOns}").FontSize(baseFont - 1).FontColor(Colors.Grey.Darken2);
                                }

                                if (!string.IsNullOrWhiteSpace(line.Note))
                                {
                                    c.Item().Text($"Ghi chú: {line.Note}").Italic().FontSize(baseFont - 1).FontColor(Colors.Grey.Darken2);
                                }

                                if (k80)
                                {
                                    c.Item().Text($"@ {Money(line.UnitPrice)}").FontSize(baseFont - 1).FontColor(Colors.Grey.Darken2);
                                }
                            });
                            table.Cell().PaddingTop(2).AlignCenter().Text(line.Quantity.ToString(Vi));
                            if (!k80)
                            {
                                table.Cell().PaddingTop(2).AlignRight().Text(Money(line.UnitPrice));
                            }

                            table.Cell().PaddingTop(2).AlignRight().Text(Money(line.LineTotal));
                        }
                    });

                    col.Item().PaddingVertical(3).LineHorizontal(0.5f);
                    AddTotalRow(col, "Tạm tính", Money(model.Subtotal));
                    if (model.DiscountAmount != 0)
                    {
                        AddTotalRow(col, "Giảm giá", "-" + Money(model.DiscountAmount));
                    }

                    if (model.TaxRatePercent > 0)
                    {
                        var label = model.PricesIncludeTax
                            ? $"Thuế ({model.TaxRatePercent.ToString("0.##", Vi)}%, đã gồm)"
                            : $"Thuế ({model.TaxRatePercent.ToString("0.##", Vi)}%)";
                        AddTotalRow(col, label, Money(model.TaxAmount));
                    }

                    if (model.RoundingAmount != 0)
                    {
                        AddTotalRow(col, "Làm tròn", Money(model.RoundingAmount));
                    }

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("TỔNG CỘNG").Bold().FontSize(baseFont + 2);
                        r.RelativeItem().AlignRight().Text(Money(model.TotalAmount)).Bold().FontSize(baseFont + 2);
                    });
                    AddTotalRow(col, "Thanh toán", $"{model.PaymentMethod} – {model.PaymentStatus}");
                    col.Item().PaddingVertical(3).LineHorizontal(0.5f);
                    if (!string.IsNullOrWhiteSpace(model.TrackingUrl))
                    {
                        col.Item().AlignCenter().Text($"Theo dõi đơn: {model.TrackingUrl}").FontSize(baseFont - 2);
                    }

                    col.Item().AlignCenter().Text(string.IsNullOrWhiteSpace(model.Footer) ? "Cảm ơn quý khách!" : model.Footer).Italic();
                });
            });
        }).GeneratePdf();
    }

    private static void AddTotalRow(ColumnDescriptor col, string label, string value) =>
        col.Item().Row(r =>
        {
            r.RelativeItem().Text(label);
            r.RelativeItem().AlignRight().Text(value);
        });
}
