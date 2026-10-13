using System.Net;
using System.Text;

namespace OfficeHealthCheck.Core;

/// <summary>Отчет като самостоятелен HTML файл, готов за печат или PDF (A4).</summary>
public static class HtmlReport
{
    public static string Render(HealthReport report)
    {
        var sb = new StringBuilder();
        string E(string s) => WebUtility.HtmlEncode(s);

        sb.Append("""
<!doctype html>
<html lang="bg">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
""");
        sb.Append($"<title>Проверка на {E(report.ComputerName)} — {Format.Date(report.GeneratedAt)}</title>\n");
        sb.Append("""
<style>
  :root{ --ink:#1b2430; --muted:#5b6675; --line:#dde3ea; --paper:#ffffff; --band:#f4f6f9;
         --ok:#1e7f4f; --ok-bg:#e6f4ec; --warn:#9a6200; --warn-bg:#fff4dc; --fail:#b42318; --fail-bg:#fde9e7;
         --info:#33568c; --info-bg:#e9f0fb; --skip:#5b6675; --skip-bg:#eef1f4; }
  *{ box-sizing:border-box }
  body{ margin:0; background:var(--paper); color:var(--ink); font:15px/1.5 "Segoe UI",system-ui,sans-serif }
  .page{ max-width:900px; margin:0 auto; padding:32px 24px 48px }
  header{ display:flex; justify-content:space-between; gap:24px; flex-wrap:wrap; align-items:flex-end;
          border-bottom:2px solid var(--ink); padding-bottom:16px }
  h1{ margin:0; font-size:26px; line-height:1.2 }
  .meta{ color:var(--muted); font-size:14px; text-align:right }
  .overall{ margin:24px 0; padding:18px 20px; border-radius:10px; font-size:18px; font-weight:600 }
  .overall small{ display:block; font-size:14px; font-weight:400; margin-top:4px }
  .counts{ display:flex; gap:20px; flex-wrap:wrap; margin-top:10px; font-size:14px; font-weight:400 }
  h2{ font-size:15px; margin:28px 0 8px; color:var(--muted); font-weight:600 }
  table{ width:100%; border-collapse:collapse }
  td{ padding:12px 10px; border-top:1px solid var(--line); vertical-align:top }
  td.st{ width:130px }
  .pill{ display:inline-block; padding:3px 10px; border-radius:99px; font-size:13px; font-weight:600; white-space:nowrap }
  .t{ font-weight:600 }
  .advice{ margin-top:4px; color:var(--muted) }
  ul.details{ margin:6px 0 0; padding-left:18px; color:var(--muted); font-size:13.5px }
  .s-Ok{ color:var(--ok); background:var(--ok-bg) } .s-Warning{ color:var(--warn); background:var(--warn-bg) }
  .s-Fail{ color:var(--fail); background:var(--fail-bg) } .s-Info{ color:var(--info); background:var(--info-bg) }
  .s-Skipped{ color:var(--skip); background:var(--skip-bg) }
  footer{ margin-top:36px; color:var(--muted); font-size:13px }
  @page{ size:A4; margin:14mm }
  @media print{ .page{ padding:0 } tr{ break-inside:avoid } }
</style>
</head>
<body>
<div class="page">
""");

        sb.Append("<header><div><h1>Проверка на компютъра</h1>");
        sb.Append($"<div>{E(report.ComputerName)} · {E(report.UserName)}</div></div>");
        sb.Append($"<div class=\"meta\">{E(report.OsDescription)}<br>{report.GeneratedAt:dd.MM.yyyy HH:mm}</div></header>\n");

        var overall = report.Overall;
        sb.Append($"<div class=\"overall s-{overall}\">{E(StatusText.OverallHeadline(overall))}");
        sb.Append("<div class=\"counts\">");
        foreach (var s in new[] { CheckStatus.Fail, CheckStatus.Warning, CheckStatus.Ok, CheckStatus.Info, CheckStatus.Skipped })
        {
            int n = report.Count(s);
            if (n > 0) sb.Append($"<span>{E(StatusText.Label(s))}: {n}</span>");
        }
        sb.Append("</div></div>\n");

        foreach (var group in report.Results.GroupBy(r => r.Category))
        {
            sb.Append($"<h2>{E(group.Key)}</h2>\n<table>\n");
            foreach (var r in group.OrderBy(r => r.Status))
            {
                sb.Append("<tr>");
                sb.Append($"<td class=\"st\"><span class=\"pill s-{r.Status}\">{E(StatusText.Label(r.Status))}</span></td>");
                sb.Append($"<td><div class=\"t\">{E(r.Title)}</div><div>{E(r.Summary)}</div>");
                if (!string.IsNullOrEmpty(r.Advice)) sb.Append($"<div class=\"advice\">{E(r.Advice)}</div>");
                if (r.Details is { Count: > 0 })
                {
                    sb.Append("<ul class=\"details\">");
                    foreach (var d in r.Details) sb.Append($"<li>{E(d)}</li>");
                    sb.Append("</ul>");
                }
                sb.Append("</td></tr>\n");
            }
            sb.Append("</table>\n");
        }

        sb.Append("<footer>Office Health Check · github.com/waitingboy-collab/office-health-check</footer>\n");
        sb.Append("</div>\n</body>\n</html>\n");
        return sb.ToString();
    }
}
