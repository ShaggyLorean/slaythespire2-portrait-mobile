using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Sts2Portrait.Patches;

public static class MenuLayout
{
    private const float BgW = 2560f;
    private const float BgH = 1200f;

    public static void Apply(NMainMenu menu)
    {
        Diagnostics.DevTools.Ensure();
        var canvas = PortraitConfig.CanvasSize;
        var center = canvas * 0.5f;

        var bgContainer = menu.GetNodeOrNull<Control>("MainMenuBg/BgContainer");
        float parentScale = 1f;
        if (bgContainer is not null)
        {
            float cover = Mathf.Max(canvas.X / BgW, canvas.Y / BgH);
            parentScale = cover;
            bgContainer.PivotOffset = new Vector2(BgW, BgH) * 0.5f;
            bgContainer.Scale = Vector2.One * cover;
            bgContainer.Position = center - new Vector2(BgW, BgH) * 0.5f;
        }

        var logo = menu.FindChild("Logo", recursive: true, owned: false) as Node2D;
        if (logo is not null)
        {
            float logoGlobalScale = 0.42f;
            logo.Scale = Vector2.One * (logoGlobalScale / parentScale);
            logo.GlobalPosition = new Vector2(center.X - 470f, canvas.Y * 0.20f);
            logo.Visible = true;
            logo.Modulate = new Color(logo.Modulate.R, logo.Modulate.G, logo.Modulate.B, 1f);
        }

        var buttons = menu.GetNodeOrNull<Control>("MainMenuTextButtons");
        if (buttons is not null)
        {
            buttons.AnchorLeft = buttons.AnchorTop = buttons.AnchorRight = buttons.AnchorBottom = 0f;
            float groupW = buttons.Size.X > 0 ? buttons.Size.X : 269f;
            buttons.Position = new Vector2(center.X - groupW * 0.5f, canvas.Y * 0.55f);
        }

        PortraitMod.Log($"menu layout: canvas={canvas} logo={(logo is null ? "?" : "ok")} bg={(bgContainer is null ? "?" : "ok")}");
    }

    public static NMainMenu? FindMenu(Node from)
    {
        for (Node? n = from; n is not null; n = n.GetParent())
            if (n is NMainMenu m) return m;
        return null;
    }
}

[HarmonyPatch(typeof(NMainMenu), "_Ready")]
public static class MainMenuReadyLayoutPatch
{
    public static void Postfix(NMainMenu __instance)
    {
        var menu = __instance;
        menu.GetTree().CreateTimer(0.6).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(menu) && menu.IsInsideTree())
                MenuLayout.Apply(menu);
        };
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenuBg), "OnWindowChange")]
public static class MainMenuBgWindowChangePatch
{
    public static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenuBg __instance)
    {
        var menu = MenuLayout.FindMenu(__instance);
        if (menu is not null)
            MenuLayout.Apply(menu);
    }
}
