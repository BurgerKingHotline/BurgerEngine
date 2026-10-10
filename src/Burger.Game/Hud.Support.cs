using System.Globalization;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Windows opened by the support diplomatic actions: send volunteers, send equipment (once or monthly).</summary>
public sealed partial class Hud
{
    private string? _supportDialog;     // "volunteers" or "lend_lease"
    private Country? _supportTarget;
    private int _volunteerCount = 1;
    private string? _equipKey;
    private string _equipAmount = "100";

    public void OpenSupportFromOutside(string kind, Country target) => OpenSupportDialog(kind, target);

    private void OpenSupportDialog(string kind, Country target)
    {
        _supportDialog = kind;
        _supportTarget = target;
        _volunteerCount = 1;
        _equipKey = null;
    }

    private void DrawSupportDialog()
    {
        var player = G.World.Player;
        if (_supportDialog == null || _supportTarget == null || player == null) return;
        if (Ui.Left != LeftPanel.Country || Ui.SelectedCountry != _supportTarget || !_supportTarget.Exists) { _supportDialog = null; return; }
        var anchor = LeftRect(2000);
        float w = 420 * S;
        var r = new Rectangle(anchor.X + anchor.Width + 8 * S, anchor.Y, w, Math.Min(anchor.Height, 620 * S));
        Gui.Panel(r, _supportDialog == "volunteers" ? $"Volunteers for {_supportTarget.Name}" : $"Equipment for {_supportTarget.Name}");
        if (Gui.CloseButton(r)) { _supportDialog = null; return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, iw = w - 24 * S;
        if (_supportDialog == "volunteers") DrawVolunteerDialog(player, _supportTarget, r, x, y, iw);
        else DrawEquipmentDialog(player, _supportTarget, r, x, y, iw);
    }

    private void DrawVolunteerDialog(Country player, Country c, Rectangle r, float x, float y, float w)
    {
        var sup = G.Support;
        int max = sup.MaxVolunteers(player), abroad = sup.VolunteersOf(player).Count();
        int free = Math.Max(0, max - abroad);
        int idle = G.World.Divisions.Count(d => d.Owner == player && d.VolunteerFrom == null && !d.InBattle);
        y += Gui.Wrapped($"Volunteer divisions land in {c.Name}'s capital and fight in its wars without us joining them. They come home when {c.Name} is at peace. " +
                         $"We may have {max} abroad ({abroad} now).", x, y, w, 14) + 10 * S;
        int can = Math.Min(free, idle);
        _volunteerCount = Math.Clamp(_volunteerCount, can > 0 ? 1 : 0, Math.Max(0, can));
        Gui.Label("Divisions to send:", x, y + 4 * S, 15);
        float bx = x + 150 * S;
        if (Gui.Button(new Rectangle(bx, y, 30 * S, 26 * S), "-", _volunteerCount > 1, null, false, 16)) _volunteerCount--;
        Gui.Label($"{_volunteerCount}", bx + 42 * S, y + 4 * S, 16, Gui.Gold, FontKind.Bold);
        if (Gui.Button(new Rectangle(bx + 70 * S, y, 30 * S, 26 * S), "+", _volunteerCount < can, null, false, 16)) _volunteerCount++;
        if (Gui.Button(new Rectangle(bx + 106 * S, y, 50 * S, 26 * S), "Max", can > 0, null, false, 13)) _volunteerCount = can;
        y += 36 * S;
        var reason = sup.CannotSendVolunteers(player, c);
        if (Gui.Button(new Rectangle(x, y, w, 30 * S), $"Send {_volunteerCount} division(s)", reason == null && _volunteerCount > 0, reason ?? "Divisions not in an army are sent first", false, 15))
        {
            int sent = sup.SendVolunteers(player, c, _volunteerCount);
            Ui.SetStatus(sent > 0 ? $"{sent} volunteer division(s) sent to {c.Name}" : "No division could be sent");
        }
        y += 40 * S;
        var here = sup.VolunteersOf(player).Where(d => d.Owner == c).ToList();
        Gui.Section($"Our volunteers in {c.Name} ({here.Count})", x, y, w);
        y += 26 * S;
        foreach (var d in here.Take(12))
        {
            Gui.Label(Gui.Fit($"{d.Name} - {d.Location.DisplayName}  {d.StrengthFraction:P0}", w - 90 * S, 14), x, y + 2 * S, 14);
            if (Gui.Button(new Rectangle(x + w - 80 * S, y, 80 * S, 22 * S), "Recall", true, null, false, 12)) sup.Recall(d);
            y += 26 * S;
        }
    }

    private void DrawEquipmentDialog(Country player, Country c, Rectangle r, float x, float y, float w)
    {
        var sup = G.Support;
        var reason = sup.CannotLendLease(player, c);
        if (reason != null) { Gui.Wrapped(reason, x, y, w, 14, Gui.Bad); return; }
        var items = player.Stockpile.Where(kv => kv.Value >= 1 || sup.Deals.Any(d => d.From == player && d.To == c && d.Equipment.Key == kv.Key))
            .OrderBy(kv => G.Data.Equipment.TryGetValue(kv.Key, out var e) ? e.Category : "").ThenBy(kv => kv.Key).ToList();
        if (items.Count == 0) { Gui.Wrapped("Our stockpile is empty. Factories add to it (Production menu).", x, y, w, 14, Gui.TextDim); return; }
        _equipKey ??= items[0].Key;
        float listH = Math.Min(items.Count * 28 * S, r.Height * 0.42f);
        float top = Gui.BeginScroll("supequip", new Rectangle(x, y, w, listH), items.Count * 28 * S);
        float ly = top;
        foreach (var (key, have) in items)
        {
            string name = G.Data.Equipment.TryGetValue(key, out var e) ? e.Name : Localisation.GetOrPretty(key);
            var deal = G.Data.Equipment.TryGetValue(key, out var et) ? sup.DealOf(player, c, et) : null;
            string label = $"{name}   {Math.Floor(have):0}" + (deal != null ? $"   ({deal.Share:P0} monthly)" : "");
            if (Gui.Button(new Rectangle(x, ly, w - 10 * S, 25 * S), Gui.Fit(label, w - 24 * S, 14), true, null, _equipKey == key, 14)) _equipKey = key;
            ly += 28 * S;
        }
        Gui.EndScroll();
        y += listH + 10 * S;
        if (_equipKey == null) return;
        double stock = Math.Floor(player.Stockpile.GetValueOrDefault(_equipKey));
        string ename = G.Data.Equipment.TryGetValue(_equipKey, out var sel) ? sel.Name : Localisation.GetOrPretty(_equipKey);
        Gui.Section(ename, x, y, w);
        y += 28 * S;
        // one-time shipment
        Gui.Label("Send once:", x, y + 4 * S, 15);
        Gui.TextField("supamount", new Rectangle(x + 100 * S, y, 90 * S, 26 * S), ref _equipAmount, 9, "amount");
        double.TryParse(_equipAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount);
        float bx = x + 198 * S;
        foreach (var (lbl, delta) in new[] { ("-100", -100.0), ("+100", 100.0), ("+1k", 1000.0) })
        {
            if (Gui.Button(new Rectangle(bx, y, 44 * S, 26 * S), lbl, true, null, false, 12))
                _equipAmount = Math.Clamp(amount + delta, 0, stock).ToString("0", CultureInfo.InvariantCulture);
            bx += 47 * S;
        }
        if (Gui.Button(new Rectangle(bx, y, w - (bx - x), 26 * S), "All", true, null, false, 12)) _equipAmount = stock.ToString("0", CultureInfo.InvariantCulture);
        y += 34 * S;
        double send = Math.Min(Math.Floor(amount), stock);
        if (Gui.Button(new Rectangle(x, y, w, 30 * S), $"Ship {send:0} {ename} now", send >= 1, $"We have {stock:0}", false, 15))
        {
            var err = sup.SendOnce(player, c, _equipKey, send);
            Ui.SetStatus(err ?? $"Shipped {send:0} {ename} to {c.Name}");
        }
        y += 42 * S;
        // monthly lend-lease
        if (sel == null) return;
        var cur = sup.DealOf(player, c, sel)?.Share ?? 0;
        Gui.Label("Every month:", x, y + 4 * S, 15);
        float mx = x + 110 * S;
        foreach (var sh in new[] { 0.0, 0.05, 0.1, 0.25, 0.5 })
        {
            bool on = Math.Abs(cur - sh) < 0.001;
            if (Gui.Button(new Rectangle(mx, y, 54 * S, 26 * S), sh == 0 ? "off" : $"{sh:P0}", true, sh == 0 ? "Stop the monthly shipments" : $"Send {sh:P0} of our {ename} stockpile every month", on, 12))
            {
                var err = sup.SetLendLease(player, c, sel, sh);
                if (err != null) Ui.SetStatus(err);
            }
            mx += 58 * S;
        }
    }
}
