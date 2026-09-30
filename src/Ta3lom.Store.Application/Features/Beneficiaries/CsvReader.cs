using System.Text;

namespace Ta3lom.Store.Application.Features.Beneficiaries;

/// Minimal RFC 4180 reader (quoted fields, doubled quotes, CRLF or LF, UTF-8 with or without BOM) for the beneficiary import.
public static class CsvReader
{
    public static List<string[]> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add([.. fields]);
                    fields.Clear();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add([.. fields]);
        }

        return rows;
    }
}
