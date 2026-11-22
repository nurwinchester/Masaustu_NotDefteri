using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NotDefteri
{
    public enum AiAction
    {
        Polish,
        Summarize,
        ExtractTodos,
        Professional,
        Custom
    }

    /// <summary>
    /// Çevrimdışı akıllı metin düzenleyici ve isteğe bağlı Yapay Zeka (Gemini / OpenAI / Ollama) istemcisi.
    /// </summary>
    static class TextAssistant
    {
        const double NormalSize = 14, H1Size = 22, H2Size = 17;

        static readonly Regex RxUrl = new Regex(@"https?://\S+", RegexOptions.IgnoreCase);
        static readonly Regex RxNumbered = new Regex(@"^\s*(\d+)[\.\)]\s+(.+)$");
        static readonly Regex RxTodoChecked = new Regex(@"^\s*(?:[-*•]\s*)?\[[xX✓✔]\]\s*(.*)$");
        static readonly Regex RxTodoUnchecked = new Regex(@"^\s*(?:[-*•]\s*)?\[\s?\]\s*(.*)$");
        static readonly Regex RxTodoSymbol = new Regex(@"^\s*([☐☑✓✔])\s*(.*)$");
        static readonly Regex RxBullet = new Regex(@"^\s*[-*•–—]\s+(.+)$");
        static readonly Regex RxHeading = new Regex(@"^\s*(#{1,3})\s+(.+)$");
        static readonly Regex RxInlineToken = new Regex(@"(\*\*[^*\r\n]+\*\*|__[^_\r\n]+__|~~[^~\r\n]+~~|`[^`\r\n]+`|\*[^*\r\n]+\*)");

        static readonly string[] TodoSectionKeywords =
        {
            "yapılacak", "yapilacak", "görev", "gorev", "to-do", "todo", "kontrol listesi", "checklist", "aksiyon", "işler", "isler"
        };

        // ------------------------------------------------------------------ belge -> markdown metin

        /// <summary>FlowDocument içeriğini başlık, liste ve to-do kutularını koruyarak sade Markdown metnine dönüştürür.</summary>
        public static string DocumentToMarkdown(FlowDocument doc)
        {
            if (doc == null) return "";
            var sb = new StringBuilder();
            AppendBlocksAsMarkdown(doc.Blocks, sb);
            return sb.ToString().TrimEnd();
        }

        static void AppendBlocksAsMarkdown(BlockCollection blocks, StringBuilder sb)
        {
            foreach (Block b in blocks)
            {
                var p = b as Paragraph;
                if (p != null)
                {
                    AppendParagraphAsMarkdown(p, sb, null);
                    continue;
                }
                var l = b as List;
                if (l != null)
                {
                    int idx = l.StartIndex > 0 ? l.StartIndex : 1;
                    bool numbered = l.MarkerStyle == TextMarkerStyle.Decimal ||
                                    l.MarkerStyle == TextMarkerStyle.LowerLatin ||
                                    l.MarkerStyle == TextMarkerStyle.UpperLatin;
                    foreach (ListItem li in l.ListItems)
                    {
                        string prefix = numbered ? (idx + ". ") : "- ";
                        bool first = true;
                        foreach (Block lb in li.Blocks)
                        {
                            var lp = lb as Paragraph;
                            if (lp != null)
                            {
                                AppendParagraphAsMarkdown(lp, sb, first ? prefix : "  ");
                                first = false;
                            }
                            else
                            {
                                AppendBlocksAsMarkdown(new Block[] { lb }, sb);
                            }
                        }
                        idx++;
                    }
                    continue;
                }
                var s = b as Section;
                if (s != null)
                {
                    AppendBlocksAsMarkdown(s.Blocks, sb);
                }
            }
        }

        static void AppendBlocksAsMarkdown(IEnumerable<Block> blocks, StringBuilder sb)
        {
            foreach (var b in blocks)
            {
                var p = b as Paragraph;
                if (p != null) AppendParagraphAsMarkdown(p, sb, null);
                var s = b as Section;
                if (s != null) AppendBlocksAsMarkdown(s.Blocks, sb);
            }
        }

        static void AppendParagraphAsMarkdown(Paragraph p, StringBuilder sb, string listPrefix)
        {
            CheckBox cb = FindTodoCheckBox(p);
            if (cb != null)
            {
                string txt = GetParagraphTextAfterTodo(p).Trim();
                sb.Append(cb.IsChecked == true ? "- [x] " : "- [ ] ");
                sb.AppendLine(txt);
                return;
            }

            string text = new TextRange(p.ContentStart, p.ContentEnd).Text.Replace("\r", "").TrimEnd();
            int level = DetectHeadingLevel(p);
            if (listPrefix != null)
            {
                sb.Append(listPrefix);
                sb.AppendLine(text.Trim());
            }
            else if (level == 1 && text.Trim().Length > 0)
            {
                sb.Append("# ");
                sb.AppendLine(text.Trim());
            }
            else if (level == 2 && text.Trim().Length > 0)
            {
                sb.Append("## ");
                sb.AppendLine(text.Trim());
            }
            else
            {
                sb.AppendLine(text);
            }
        }

        static CheckBox FindTodoCheckBox(Paragraph p)
        {
            if (p == null) return null;
            foreach (Inline i in p.Inlines)
            {
                var c = i as InlineUIContainer;
                if (c != null) return c.Child as CheckBox;
                var r = i as Run;
                if (r != null && string.IsNullOrEmpty(r.Text)) continue;
                return null;
            }
            return null;
        }

        static string GetParagraphTextAfterTodo(Paragraph p)
        {
            foreach (Inline i in p.Inlines)
            {
                var c = i as InlineUIContainer;
                if (c != null && c.Child is CheckBox)
                    return new TextRange(c.ElementEnd, p.ContentEnd).Text.Replace("\r", "");
            }
            return new TextRange(p.ContentStart, p.ContentEnd).Text.Replace("\r", "");
        }

        static int DetectHeadingLevel(Paragraph p)
        {
            object fs = p.ReadLocalValue(TextElement.FontSizeProperty);
            if (fs is double)
            {
                double v = (double)fs;
                if (v >= 20) return 1;
                if (v >= 16) return 2;
            }
            return 0;
        }

        // ------------------------------------------------------------------ çevrimdışı akıllı düzenleme

        /// <summary>
        /// Dağınık metni tek tıkla başlık, madde, to-do kutusu, noktalama ve Türkçe büyük harf kurallarıyla düzenler.
        /// </summary>
        public static string SmartFormat(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            var lines = SplitLines(raw);
            var outLines = new List<string>();

            int nonEmptyCount = lines.Count(l => l.Trim().Length > 0);
            bool seenFirstContent = false;
            bool inTodoSection = false;
            int blankRun = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    inTodoSection = false;
                    blankRun++;
                    if (seenFirstContent && blankRun == 1) outLines.Add("");
                    continue;
                }
                blankRun = 0;

                // 1) Mevcut Markdown başlığı (# / ##)
                var mh = RxHeading.Match(line);
                if (mh.Success)
                {
                    seenFirstContent = true;
                    string hashes = mh.Groups[1].Value.Length == 1 ? "#" : "##";
                    string title = CleanTitle(mh.Groups[2].Value);
                    inTodoSection = IsTodoHeader(title);
                    outLines.Add(hashes + " " + title);
                    continue;
                }

                // 2) To-do kalıpları ([x], [ ], [], ☐, ☑)
                var mc = RxTodoChecked.Match(line);
                if (mc.Success)
                {
                    seenFirstContent = true;
                    outLines.Add("- [x] " + CleanSentence(mc.Groups[1].Value, false));
                    continue;
                }
                var mu = RxTodoUnchecked.Match(line);
                if (mu.Success)
                {
                    seenFirstContent = true;
                    outLines.Add("- [ ] " + CleanSentence(mu.Groups[1].Value, false));
                    continue;
                }
                var ms = RxTodoSymbol.Match(line);
                if (ms.Success)
                {
                    seenFirstContent = true;
                    bool done = ms.Groups[1].Value != "☐";
                    outLines.Add((done ? "- [x] " : "- [ ] ") + CleanSentence(ms.Groups[2].Value, false));
                    continue;
                }

                // 3) Numaralı liste (1. / 1))
                var mn = RxNumbered.Match(line);
                if (mn.Success)
                {
                    seenFirstContent = true;
                    outLines.Add(mn.Groups[1].Value + ". " + CleanSentence(mn.Groups[2].Value, false));
                    continue;
                }

                // 4) Madde işaretli liste (- / * / •)
                var mb = RxBullet.Match(line);
                if (mb.Success)
                {
                    seenFirstContent = true;
                    string item = CleanSentence(mb.Groups[1].Value, false);
                    outLines.Add((inTodoSection ? "- [ ] " : "- ") + item);
                    continue;
                }

                // 5) Sonu ":" ile biten kısa bölüm başlığı (ör. "Yapılacaklar:")
                bool hasNextContent = HasFollowingContent(lines, i);
                if (line.EndsWith(":") && line.Length <= 58 && hasNextContent && !RxUrl.IsMatch(line))
                {
                    seenFirstContent = true;
                    string title = CleanTitle(line.TrimEnd(':'));
                    inTodoSection = IsTodoHeader(title);
                    if (outLines.Count > 0 && outLines[outLines.Count - 1] != "") outLines.Add("");
                    outLines.Add("## " + title);
                    continue;
                }

                // 6) İlk satır kısa ve noktasızsa ana başlık (H1) yap
                if (!seenFirstContent && nonEmptyCount >= 2 && line.Length <= 62 && !EndsWithSentencePunctuation(line) && !RxUrl.IsMatch(line))
                {
                    seenFirstContent = true;
                    string title = CleanTitle(line);
                    inTodoSection = IsTodoHeader(title);
                    outLines.Add("# " + title);
                    continue;
                }

                seenFirstContent = true;

                // 7) "Yapılacaklar" başlığının altındaki düz kısa satırları to-do kutusuna çevir
                if (inTodoSection && line.Length <= 120)
                {
                    outLines.Add("- [ ] " + CleanSentence(line, false));
                    continue;
                }

                // 8) Normal paragraf: noktalama ve büyük harf düzelt
                outLines.Add(CleanSentence(line, true));
            }

            while (outLines.Count > 0 && outLines[outLines.Count - 1] == "")
                outLines.RemoveAt(outLines.Count - 1);

            return string.Join("\n", outLines.ToArray());
        }

        /// <summary>Sadece yazım, Türkçe büyük/küçük harf ve boşluk/noktalama düzeltmesi yapar; yapıyı korur.</summary>
        public static string FixSpellingAndSpacing(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            var lines = SplitLines(raw);
            var outLines = new List<string>();
            int blankRun = 0;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    blankRun++;
                    if (outLines.Count > 0 && blankRun == 1) outLines.Add("");
                    continue;
                }
                blankRun = 0;

                var mh = RxHeading.Match(line);
                if (mh.Success) { outLines.Add(mh.Groups[1].Value + " " + CleanTitle(mh.Groups[2].Value)); continue; }
                var mc = RxTodoChecked.Match(line);
                if (mc.Success) { outLines.Add("- [x] " + CleanSentence(mc.Groups[1].Value, false)); continue; }
                var mu = RxTodoUnchecked.Match(line);
                if (mu.Success) { outLines.Add("- [ ] " + CleanSentence(mu.Groups[1].Value, false)); continue; }
                var mn = RxNumbered.Match(line);
                if (mn.Success) { outLines.Add(mn.Groups[1].Value + ". " + CleanSentence(mn.Groups[2].Value, false)); continue; }
                var mb = RxBullet.Match(line);
                if (mb.Success) { outLines.Add("- " + CleanSentence(mb.Groups[1].Value, false)); continue; }

                outLines.Add(CleanSentence(line, false));
            }

            while (outLines.Count > 0 && outLines[outLines.Count - 1] == "")
                outLines.RemoveAt(outLines.Count - 1);

            return string.Join("\n", outLines.ToArray());
        }

        /// <summary>Satırları düzenli madde listesine çevirir (başlıkları korur).</summary>
        public static string ConvertToBullets(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            var outLines = new List<string>();
            foreach (var rawLine in SplitLines(raw))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                var mh = RxHeading.Match(line);
                if (mh.Success) { outLines.Add(mh.Groups[1].Value + " " + CleanTitle(mh.Groups[2].Value)); continue; }
                string stripped = StripPrefix(line);
                if (stripped.Length == 0) continue;
                outLines.Add("- " + CleanSentence(stripped, false));
            }
            return string.Join("\n", outLines.ToArray());
        }

        /// <summary>Satırları etkileşimli to-do listesine çevirir (başlıkları ve işaretli maddeleri korur).</summary>
        public static string ConvertToTodos(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            var outLines = new List<string>();
            foreach (var rawLine in SplitLines(raw))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                var mh = RxHeading.Match(line);
                if (mh.Success) { outLines.Add(mh.Groups[1].Value + " " + CleanTitle(mh.Groups[2].Value)); continue; }
                var mc = RxTodoChecked.Match(line);
                if (mc.Success) { outLines.Add("- [x] " + CleanSentence(mc.Groups[1].Value, false)); continue; }
                var ms = RxTodoSymbol.Match(line);
                if (ms.Success && ms.Groups[1].Value != "☐") { outLines.Add("- [x] " + CleanSentence(ms.Groups[2].Value, false)); continue; }
                string stripped = StripPrefix(line);
                if (stripped.Length == 0) continue;
                outLines.Add("- [ ] " + CleanSentence(stripped, false));
            }
            return string.Join("\n", outLines.ToArray());
        }

        /// <summary>İşaretlenmiş to-do maddelerini listenin altına toplar.</summary>
        public static string SortCompletedTodosBottom(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            var lines = SplitLines(raw);
            var result = new List<string>();
            var pending = new List<string>();
            var done = new List<string>();

            Action flush = () =>
            {
                if (pending.Count == 0 && done.Count == 0) return;
                result.AddRange(pending);
                result.AddRange(done);
                pending.Clear();
                done.Clear();
            };

            foreach (var l in lines)
            {
                string t = l.Trim();
                if (RxTodoChecked.IsMatch(t)) done.Add(t);
                else if (RxTodoUnchecked.IsMatch(t)) pending.Add(t);
                else
                {
                    flush();
                    result.Add(l);
                }
            }
            flush();
            return string.Join("\n", result.ToArray());
        }

        /// <summary>Tüm biçimlendirmeyi temizleyip sade metin satırlarına indirger.</summary>
        public static string SimplifyPlain(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            var outLines = new List<string>();
            int blankRun = 0;
            foreach (var rawLine in SplitLines(raw))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    blankRun++;
                    if (outLines.Count > 0 && blankRun == 1) outLines.Add("");
                    continue;
                }
                blankRun = 0;
                line = StripPrefix(line);
                line = Regex.Replace(line, @"\*\*([^*]+)\*\*", "$1");
                line = Regex.Replace(line, @"__([^_]+)__", "$1");
                line = Regex.Replace(line, @"~~([^~]+)~~", "$1");
                line = Regex.Replace(line, @"`([^`]+)`", "$1");
                line = Regex.Replace(line, @"\*([^*]+)\*", "$1");
                outLines.Add(CollapseSpaces(line));
            }
            return string.Join("\n", outLines.ToArray());
        }

        static string[] SplitLines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }

        static bool HasFollowingContent(string[] lines, int idx)
        {
            for (int j = idx + 1; j < lines.Length; j++)
                if (lines[j].Trim().Length > 0) return true;
            return false;
        }

        static bool IsTodoHeader(string title)
        {
            string low = (title ?? "").ToLower(Theme.Tr);
            foreach (var kw in TodoSectionKeywords)
                if (low.Contains(kw)) return true;
            return false;
        }

        static bool EndsWithSentencePunctuation(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            char c = s[s.Length - 1];
            return c == '.' || c == '!' || c == '?' || c == ';' || c == ':';
        }

        static string StripPrefix(string line)
        {
            var mh = RxHeading.Match(line);
            if (mh.Success) return mh.Groups[2].Value.Trim();
            var mc = RxTodoChecked.Match(line);
            if (mc.Success) return mc.Groups[1].Value.Trim();
            var mu = RxTodoUnchecked.Match(line);
            if (mu.Success) return mu.Groups[1].Value.Trim();
            var ms = RxTodoSymbol.Match(line);
            if (ms.Success) return ms.Groups[2].Value.Trim();
            var mn = RxNumbered.Match(line);
            if (mn.Success) return mn.Groups[2].Value.Trim();
            var mb = RxBullet.Match(line);
            if (mb.Success) return mb.Groups[1].Value.Trim();
            return line.Trim();
        }

        static string CollapseSpaces(string s)
        {
            return Regex.Replace((s ?? "").Trim(), @"[ \t]{2,}", " ");
        }

        static string CleanTitle(string s)
        {
            s = CollapseSpaces(s);
            if (s.Length == 0) return "";
            // Tamamı büyük harf değilse ilk harfi Türkçe kurallarına göre büyüt.
            if (!IsAllUpper(s)) s = CapitalizeFirstLetter(s);
            return s;
        }

        static bool IsAllUpper(string s)
        {
            bool hasLetter = false;
            foreach (char c in s)
            {
                if (char.IsLetter(c))
                {
                    hasLetter = true;
                    if (!char.IsUpper(c)) return false;
                }
            }
            return hasLetter;
        }

        static string CleanSentence(string s, bool addPeriodIfLong)
        {
            s = CollapseSpaces(s);
            if (s.Length == 0) return "";

            // URL'leri geçici yer tutucularla koru.
            var urls = new List<string>();
            s = RxUrl.Replace(s, m =>
            {
                urls.Add(m.Value);
                return "\u0001URL" + (urls.Count - 1) + "\u0002";
            });

            // Noktalama öncesi gereksiz boşlukları kaldır: "kelime , kelime ." -> "kelime, kelime."
            s = Regex.Replace(s, @"\s+([,.;:!?])", "$1");
            // Noktalama sonrası boşluk ekle (ondalık sayılar 3.14 ve saat 14:30 hariç)
            s = Regex.Replace(s, @"([,;!?\.\:])([^\s\d\.\,\!\?\;\:\)\]\""'»”\u0001\u0002\*])", "$1 $2");
            // Parantez içindeki baş/son boşlukları düzelt
            s = Regex.Replace(s, @"\(\s+", "(");
            s = Regex.Replace(s, @"\s+\)", ")");
            s = CollapseSpaces(s);

            // Cümle başlarını Türkçe kurallarına göre büyüt
            s = CapitalizeSentences(s);

            if (addPeriodIfLong && s.Length >= 45 && !EndsWithSentencePunctuation(s) && !s.EndsWith(")") && !s.EndsWith("\""))
                s += ".";

            for (int i = 0; i < urls.Count; i++)
                s = s.Replace("\u0001URL" + i + "\u0002", urls[i]);

            return s;
        }

        static string CapitalizeFirstLetter(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsLetter(chars[i]))
                {
                    string up = chars[i].ToString().ToUpper(Theme.Tr);
                    chars[i] = up[0];
                    break;
                }
            }
            return new string(chars);
        }

        static string CapitalizeSentences(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            bool capNext = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (capNext && char.IsLetter(c))
                {
                    sb.Append(c.ToString().ToUpper(Theme.Tr));
                    capNext = false;
                    continue;
                }
                sb.Append(c);
                if (c == '.' || c == '!' || c == '?')
                {
                    // Sadece arkasından boşluk ya da satır sonu geliyorsa yeni cümle başlat.
                    if (i + 1 >= s.Length || char.IsWhiteSpace(s[i + 1]))
                        capNext = true;
                }
                else if (char.IsDigit(c))
                {
                    capNext = false;
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ markdown -> FlowDocument

        /// <summary>Markdown formatındaki metni WPF FlowDocument bloklarına (başlık, liste, to-do, kalın/italik) çevirir.</summary>
        public static List<Block> MarkdownToBlocks(string markdown)
        {
            var blocks = new List<Block>();
            var lines = SplitLines(markdown);
            List currentList = null;
            bool currentListNumbered = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine.TrimEnd();
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    currentList = null;
                    if (blocks.Count > 0)
                    {
                        var lastP = blocks[blocks.Count - 1] as Paragraph;
                        if (lastP == null || lastP.Inlines.Count > 0)
                            blocks.Add(new Paragraph());
                    }
                    continue;
                }

                // Başlıklar (# / ##)
                var mh = RxHeading.Match(trimmed);
                if (mh.Success)
                {
                    currentList = null;
                    int lvl = mh.Groups[1].Value.Length == 1 ? 1 : 2;
                    var p = MakeInlineParagraph(mh.Groups[2].Value.Trim());
                    double sz = lvl == 1 ? H1Size : H2Size;
                    FontWeight fw = lvl == 1 ? FontWeights.Bold : FontWeights.SemiBold;
                    p.FontSize = sz;
                    p.FontWeight = fw;
                    p.Margin = new Thickness(0, lvl == 1 ? 10 : 6, 0, 3);
                    blocks.Add(p);
                    continue;
                }

                // To-do kutuları
                var mc = RxTodoChecked.Match(trimmed);
                if (mc.Success)
                {
                    currentList = null;
                    blocks.Add(MakeTodoParagraph(mc.Groups[1].Value.Trim(), true));
                    continue;
                }
                var mu = RxTodoUnchecked.Match(trimmed);
                if (mu.Success)
                {
                    currentList = null;
                    blocks.Add(MakeTodoParagraph(mu.Groups[1].Value.Trim(), false));
                    continue;
                }

                // Numaralı liste
                var mn = RxNumbered.Match(trimmed);
                if (mn.Success)
                {
                    if (currentList == null || !currentListNumbered)
                    {
                        currentList = new List { MarkerStyle = TextMarkerStyle.Decimal };
                        currentListNumbered = true;
                        blocks.Add(currentList);
                    }
                    currentList.ListItems.Add(new ListItem(MakeInlineParagraph(mn.Groups[2].Value.Trim())));
                    continue;
                }

                // Madde işaretli liste
                var mb = RxBullet.Match(trimmed);
                if (mb.Success)
                {
                    if (currentList == null || currentListNumbered)
                    {
                        currentList = new List { MarkerStyle = TextMarkerStyle.Disc };
                        currentListNumbered = false;
                        blocks.Add(currentList);
                    }
                    currentList.ListItems.Add(new ListItem(MakeInlineParagraph(mb.Groups[1].Value.Trim())));
                    continue;
                }

                // Normal paragraf
                currentList = null;
                blocks.Add(MakeInlineParagraph(trimmed));
            }

            if (blocks.Count == 0) blocks.Add(new Paragraph());
            return blocks;
        }

        static Paragraph MakeTodoParagraph(string text, bool isChecked)
        {
            var p = new Paragraph();
            var cb = new CheckBox { IsChecked = isChecked };
            p.Inlines.Add(new InlineUIContainer(cb) { BaselineAlignment = BaselineAlignment.Center });
            AddFormattedInlines(p.Inlines, text);
            if (isChecked)
            {
                var c = p.Inlines.FirstInline;
                if (c != null)
                {
                    var r = new TextRange(c.ElementEnd, p.ContentEnd);
                    if (!r.IsEmpty)
                    {
                        r.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
                        r.ApplyPropertyValue(TextElement.ForegroundProperty, Theme.B(Theme.InkSoft));
                    }
                }
            }
            return p;
        }

        static Paragraph MakeInlineParagraph(string text)
        {
            var p = new Paragraph();
            AddFormattedInlines(p.Inlines, text);
            return p;
        }

        static void AddFormattedInlines(InlineCollection inlines, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int pos = 0;
            foreach (Match m in RxInlineToken.Matches(text))
            {
                if (m.Index > pos) inlines.Add(new Run(text.Substring(pos, m.Index - pos)));
                string tok = m.Value;
                if ((tok.StartsWith("**") && tok.EndsWith("**")) || (tok.StartsWith("__") && tok.EndsWith("__")))
                {
                    inlines.Add(new Bold(new Run(tok.Substring(2, tok.Length - 4))));
                }
                else if (tok.StartsWith("~~") && tok.EndsWith("~~"))
                {
                    var r = new Run(tok.Substring(2, tok.Length - 4));
                    r.TextDecorations = TextDecorations.Strikethrough;
                    inlines.Add(r);
                }
                else if (tok.StartsWith("`") && tok.EndsWith("`"))
                {
                    var r = new Run(tok.Substring(1, tok.Length - 2));
                    r.FontFamily = new FontFamily("Consolas, Courier New");
                    r.Background = Theme.B("#16000000");
                    inlines.Add(r);
                }
                else if (tok.StartsWith("*") && tok.EndsWith("*"))
                {
                    inlines.Add(new Italic(new Run(tok.Substring(1, tok.Length - 2))));
                }
                else
                {
                    inlines.Add(new Run(tok));
                }
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) inlines.Add(new Run(text.Substring(pos)));
        }

        // ------------------------------------------------------------------ isteğe bağlı gerçek Yapay Zeka (Gemini / OpenAI / Ollama)

        public static bool IsAiConfigured()
        {
            var s = Store.Data != null ? Store.Data.Settings : null;
            if (s == null) return false;
            string prov = (s.AiProvider ?? "Gemini").Trim();
            if (string.Equals(prov, "Ollama", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(s.AiModel) || !string.IsNullOrWhiteSpace(s.AiEndpoint);
            return !string.IsNullOrWhiteSpace(s.AiApiKey);
        }

        public static string DefaultModelFor(string provider)
        {
            if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)) return "gpt-4o-mini";
            if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase)) return "llama3.2";
            return "gemini-3.5-flash-lite";
        }

        public static string[] SuggestedModelsFor(string provider)
        {
            if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
                return new[] { "gpt-4o-mini", "gpt-4.1-mini", "gpt-4o" };
            if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
                return new[] { "llama3.2", "qwen2.5", "gemma2" };
            return new[] { "gemini-3.5-flash-lite", "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.0-flash" };
        }

        public static string DefaultEndpointFor(string provider)
        {
            if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)) return "https://api.openai.com/v1/chat/completions";
            if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase)) return "http://localhost:11434/v1/chat/completions";
            return "";
        }

        public static string CleanApiKey(string raw)
        {
            string k = (raw ?? "").Trim();
            int eq = k.IndexOf('=');
            if (eq > 0 && eq < 24 && k.Substring(0, eq).ToUpperInvariant().Contains("KEY"))
                k = k.Substring(eq + 1).Trim();
            return k.Trim('"', '\'', ' ', '\r', '\n', '\t');
        }

        static string CleanModel(string raw, string provider)
        {
            string m = (raw ?? "").Trim();
            if (m.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) m = m.Substring(7);
            return string.IsNullOrWhiteSpace(m) ? DefaultModelFor(provider) : m;
        }

        public static void TestConnectionAsync(string provider, string rawApiKey, string rawModel, string endpoint, Action<bool, string> callback)
        {
            string prov = string.IsNullOrWhiteSpace(provider) ? "Gemini" : provider.Trim();
            string apiKey = CleanApiKey(rawApiKey);
            string model = CleanModel(rawModel, prov);
            string ep = (endpoint ?? "").Trim();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ConfigureNetwork();
                    string prompt = "Sadece 'Tamam' yaz.";
                    string res = string.Equals(prov, "Gemini", StringComparison.OrdinalIgnoreCase)
                        ? CallGemini(apiKey, model, prompt)
                        : CallOpenAiCompatible(prov, apiKey, model, ep, prompt);
                    if (string.IsNullOrWhiteSpace(res)) callback(false, "Boş yanıt döndü.");
                    else callback(true, "Bağlantı başarılı! (" + model + ")");
                }
                catch (WebException wex)
                {
                    App.Log(wex);
                    callback(false, ReadWebError(wex, model));
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                    callback(false, ex.Message);
                }
            });
        }

        static void ConfigureNetwork()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                ServicePointManager.DnsRefreshTimeout = 10000;
            }
            catch { }
        }

        public static void RunAiAsync(string inputMarkdown, AiAction action, string customInstruction, Action<string, string> callback)
        {
            var settings = Store.Data.Settings;
            string provider = string.IsNullOrWhiteSpace(settings.AiProvider) ? "Gemini" : settings.AiProvider.Trim();
            string apiKey = CleanApiKey(settings.AiApiKey);
            string model = CleanModel(settings.AiModel, provider);
            string endpoint = (settings.AiEndpoint ?? "").Trim();

            string prompt = BuildPrompt(action, customInstruction, inputMarkdown);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ConfigureNetwork();

                    string result;
                    if (string.Equals(provider, "Gemini", StringComparison.OrdinalIgnoreCase))
                        result = CallGemini(apiKey, model, prompt);
                    else
                        result = CallOpenAiCompatible(provider, apiKey, model, endpoint, prompt);

                    result = CleanAiOutput(result);
                    callback(result, null);
                }
                catch (WebException wex)
                {
                    string detail = ReadWebError(wex, model);
                    App.Log(wex);
                    callback(null, detail);
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                    callback(null, ex.Message);
                }
            });
        }

        static string BuildPrompt(AiAction action, string custom, string text)
        {
            string task;
            switch (action)
            {
                case AiAction.Summarize:
                    task = "Aşağıdaki notu kısa, net ve düzenli bir şekilde özetle. Önemli noktaları madde işaretleri (- ) ile vurgula.";
                    break;
                case AiAction.ExtractTodos:
                    task = "Aşağıdaki metinden yapılacak işleri/görevleri çıkar ve her birini '- [ ] görev' biçiminde yapılacaklar listesi olarak yaz. Tamamlanmış olanlar varsa '- [x] görev' olarak koru.";
                    break;
                case AiAction.Professional:
                    task = "Aşağıdaki metni daha akıcı, açık ve profesyonel bir dille yeniden düzenle; anlamı koru, yazım ve anlatım bozukluklarını gider.";
                    break;
                case AiAction.Custom:
                    task = "Kullanıcı isteği: " + (custom ?? "Metni düzenle.") + "\nBu isteğe göre aşağıdaki notu düzenle.";
                    break;
                default:
                    task = "Aşağıdaki notu düzenle ve toparla: yazım, noktalama ve büyük/küçük harf hatalarını düzelt; uygun yerlerde başlık (# / ##), madde işareti (- ) ve yapılacaklar kutusu (- [ ] / - [x]) kullanarak okunaklı hale getir. Anlamı ve bilgileri kaybetme.";
                    break;
            }
            return task +
                "\n\nKurallar:\n" +
                "- Sadece düzenlenmiş notun kendisini döndür; açıklama, giriş veya 'İşte düzenlenmiş hali:' gibi ifadeler ekleme.\n" +
                "- Orijinal metnin dilini koru (özel istekte başka dil istenmediyse).\n" +
                "- Kod bloğu (```) içine alma.\n\n" +
                "METİN:\n" + text;
        }

        static string CallGemini(string apiKey, string model, string prompt)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Gemini API anahtarı girilmemiş.");
            string url = "https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(model) + ":generateContent?key=" + Uri.EscapeDataString(apiKey);
            var payload = new Dictionary<string, object>
            {
                {
                    "contents", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "parts", new object[] { new Dictionary<string, object> { { "text", prompt } } } }
                        }
                    }
                }
            };
            string json = PostJson(url, payload, null);
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root != null && root.ContainsKey("candidates"))
            {
                var cands = root["candidates"] as object[];
                if (cands != null && cands.Length > 0)
                {
                    var first = cands[0] as Dictionary<string, object>;
                    var content = first != null && first.ContainsKey("content") ? first["content"] as Dictionary<string, object> : null;
                    var parts = content != null && content.ContainsKey("parts") ? content["parts"] as object[] : null;
                    if (parts != null && parts.Length > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var pt in parts)
                        {
                            var pd = pt as Dictionary<string, object>;
                            if (pd != null && pd.ContainsKey("text") && pd["text"] != null)
                                sb.Append(pd["text"].ToString());
                        }
                        if (sb.Length > 0) return sb.ToString();
                    }
                }
            }
            throw new InvalidOperationException("Yapay zeka yanıtı okunamadı.");
        }

        static string CallOpenAiCompatible(string provider, string apiKey, string model, string endpoint, string prompt)
        {
            string url = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpointFor(provider) : endpoint;
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("API uç noktası (endpoint) boş.");
            if (!string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("API anahtarı girilmemiş.");

            var payload = new Dictionary<string, object>
            {
                { "model", model },
                {
                    "messages", new object[]
                    {
                        new Dictionary<string, object> { { "role", "system" }, { "content", "Sen masaüstü not defteri için yardımcı bir metin düzenleyicisin. Sadece istenen düzenlenmiş metni temiz Markdown biçiminde döndür." } },
                        new Dictionary<string, object> { { "role", "user" }, { "content", prompt } }
                    }
                },
                { "temperature", 0.3 }
            };
            string json = PostJson(url, payload, string.IsNullOrWhiteSpace(apiKey) ? null : "Bearer " + apiKey);
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root != null && root.ContainsKey("choices"))
            {
                var choices = root["choices"] as object[];
                if (choices != null && choices.Length > 0)
                {
                    var c0 = choices[0] as Dictionary<string, object>;
                    var msg = c0 != null && c0.ContainsKey("message") ? c0["message"] as Dictionary<string, object> : null;
                    if (msg != null && msg.ContainsKey("content") && msg["content"] != null)
                        return msg["content"].ToString();
                }
            }
            throw new InvalidOperationException("Yapay zeka yanıtı okunamadı.");
        }

        static string PostJson(string url, object payload, string authHeader)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            byte[] data = Encoding.UTF8.GetBytes(ser.Serialize(payload));
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.KeepAlive = false;
            req.Timeout = 45000;
            req.ReadWriteTimeout = 45000;
            req.ContentLength = data.Length;
            if (!string.IsNullOrEmpty(authHeader)) req.Headers["Authorization"] = authHeader;
            using (var st = req.GetRequestStream()) st.Write(data, 0, data.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static string ReadWebError(WebException wex, string model)
        {
            if (wex.Status == WebExceptionStatus.NameResolutionFailure)
                return "Sunucuya ulaşılamadı (DNS çözülemedi). İnternet/VPN bağlantını kontrol edip tekrar dene.";
            if (wex.Status == WebExceptionStatus.Timeout)
                return "Bağlantı zaman aşımına uğradı. Tekrar dene.";
            try
            {
                var http = wex.Response as HttpWebResponse;
                if (http != null)
                {
                    string body = "";
                    using (var sr = new StreamReader(http.GetResponseStream(), Encoding.UTF8))
                        body = sr.ReadToEnd();
                    if ((int)http.StatusCode == 404)
                        return "Model bulunamadı ('" + model + "'). Ayarlardan geçerli bir model seç (ör. gemini-2.5-flash).";
                    if ((int)http.StatusCode == 401 || (int)http.StatusCode == 403)
                        return "API anahtarı geçersiz veya yetkisiz. Ayarlardan anahtarı kontrol et.";
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        var ser = new JavaScriptSerializer();
                        var obj = ser.DeserializeObject(body) as Dictionary<string, object>;
                        if (obj != null && obj.ContainsKey("error"))
                        {
                            var err = obj["error"] as Dictionary<string, object>;
                            if (err != null && err.ContainsKey("message") && err["message"] != null)
                                return err["message"].ToString();
                            if (obj["error"] != null) return obj["error"].ToString();
                        }
                        return body.Length > 260 ? body.Substring(0, 260) + "…" : body;
                    }
                }
            }
            catch { }
            return wex.Message;
        }

        static string CleanAiOutput(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string s = text.Trim();
            // Model yanlışlıkla ```markdown ... ``` sardıysa soy.
            if (s.StartsWith("```"))
            {
                int nl = s.IndexOf('\n');
                if (nl > 0) s = s.Substring(nl + 1);
                if (s.EndsWith("```")) s = s.Substring(0, s.Length - 3);
                s = s.Trim();
            }
            return s;
        }
    }
}
