// Recipes: quadrupeds (wolves, felhunter, boars, cat, bear, sheep, the Hollow Warden stag), the spider, the owl and
// static units (training dummy, totems, hunter traps, lightwell).
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class UnitRecipes
    {
        enum WolfKind { Grey, Blighted, Pet, Felhunter }

        static UnitModel Bake(QuadKit k) => UnitModels.Bake(k.Model, k.M);

        // ================================================================== wolves & felhunter

        static UnitModel Wolf(string key, WolfKind kind)
        {
            bool fel = kind == WolfKind.Felhunter;
            var k = new QuadKit(fel ? 61 : 62 + (int)kind, 0.56f, 0.6f, fel ? 0.6f : 0.55f, fel ? 0.2f : 0.18f, 0.1f, fel ? 0.046f : 0.042f,
                                0.52f, 0.54f, 0.14f, 0.22f, fel ? 0.13f : 0.115f);
            Color back, belly, legs, eyes, nose = C("#2a2a2a");
            switch (kind)
            {
                case WolfKind.Blighted: back = C("#8e8899"); belly = C("#b8b2c4"); legs = C("#4a4458"); eyes = C("#c9a8ff"); break;
                case WolfKind.Pet: back = C("#8a7a6a"); belly = C("#e8dcc8"); legs = C("#6e6052"); eyes = C("#6ab060"); break;
                case WolfKind.Felhunter: back = C("#3e2c4e"); belly = C("#5a4470"); legs = C("#2a1e36"); eyes = C("#7cf06a"); break;
                default: back = C("#8a8a90"); belly = C("#e8e0d0"); legs = C("#6a6a72"); eyes = C("#e8a030"); break;
            }
            k.Body(back, belly);
            // darker saddle
            k.M.Bone = QB.Chest; k.M.Color = Paint.Shade(back, 0.7f);
            k.M.Sphere(k.Bind[QB.Chest] + new Vector3(0f, k.BodyR * 0.45f, -k.BodyLen * 0.25f), new Vector3(k.BodyR * 0.75f, k.BodyR * 0.45f, k.BodyLen * 0.45f), 8, 4, false);
            k.Neck(back);
            // chest ruff
            k.M.Bone = QB.Neck; k.M.Color = belly;
            k.M.Blob(k.NeckBase + new Vector3(0f, -0.02f, 0.06f), new Vector3(0.12f, 0.12f, 0.1f), 0, 0.2f, 3);
            var tip = k.Head(back, belly, nose, eyes, fel ? 0.13f : 0.15f, 0.48f, kind == WolfKind.Blighted || fel);
            k.Jaw(Paint.Shade(belly, 0.9f), fel ? 0.17f : 0.19f, C("#f4f0e0"));
            if (!fel) k.Ears(back, Paint.Shade(back, 0.6f), 1f, 0.55f, 0.1f);
            k.Legs(back, legs, Paint.Shade(legs, 0.8f));
            k.Tail(back, fel ? 0.36f : 0.32f, fel ? 0.04f : 0.055f, kind == WolfKind.Grey || kind == WolfKind.Pet ? belly : (Color?)null, !fel, 0.55f);
            switch (kind)
            {
                case WolfKind.Blighted:
                {
                    // glowing violet vein cracks and crystals on the back
                    k.M.Emission = 0.9f; k.M.Color = eyes;
                    k.M.Bone = QB.Chest;
                    for (int i = 0; i < 4; i++)
                    {
                        float z = k.Bind[QB.Chest].z - 0.05f - i * 0.12f;
                        int s = (i & 1) == 0 ? 1 : -1;
                        k.M.Push().Translate(s * k.BodyR * 0.9f, k.ChestY + 0.02f, z).Rotate(0f, 0f, s * 70f);
                        k.M.Box(Vector3.zero, new Vector3(0.015f, 0.12f, 0.02f));
                        k.M.Pop();
                    }
                    k.M.Bone = QB.Back; k.M.Color = C("#b98aff");
                    for (int i = 0; i < 4; i++)
                        k.M.Spike(k.Bind[QB.Back] + new Vector3((i - 1.5f) * 0.03f, -0.03f, 0.15f - i * 0.1f), new Vector3((i - 1.5f) * 0.3f, 1f, -0.3f), 0.03f, 0.1f + (i % 2) * 0.05f, 4);
                    k.M.Emission = 0f;
                    break;
                }
                case WolfKind.Pet:
                {
                    // red scarf collar
                    k.M.Bone = QB.Neck; k.M.Color = C("#c84a3a");
                    var nb = Vector3.Lerp(k.NeckBase, k.HeadBase, 0.35f);
                    k.M.Push().Translate(nb).Rotate(-55f, 0f, 0f);
                    k.M.Torus(Vector3.zero, 0.085f, 0.025f, 10, 4);
                    k.M.Pop();
                    k.M.Blade(nb + new Vector3(0.02f, -0.04f, 0.07f), nb + new Vector3(0.05f, -0.16f, 0.1f), 0.06f);
                    break;
                }
                case WolfKind.Felhunter:
                {
                    // bone spines along the back
                    k.M.Bone = QB.Back; k.M.Color = C("#e9e0c8");
                    for (int i = 0; i < 6; i++)
                        k.M.Spike(k.Bind[QB.Back] + new Vector3(0f, -0.04f, 0.28f - i * 0.11f), new Vector3(0f, 1f, -0.5f), 0.03f, 0.12f - Mathf.Abs(i - 2.5f) * 0.015f, 4);
                    // head tentacles with glowing tips
                    k.M.Bone = QB.Head; k.M.Color = belly;
                    var hc = k.HeadBase + new Vector3(0f, k.HeadR * 0.6f, k.HeadR * 0.2f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var a = hc + new Vector3(s * k.HeadR * 0.4f, 0f, 0f);
                        var b = a + new Vector3(s * 0.08f, 0.1f, -0.22f);
                        k.M.Curve(a, a + new Vector3(s * 0.04f, 0.12f, -0.05f), b, 0.022f, 0.01f, 4, 5);
                        k.M.Emission = 1f; k.M.Color = eyes;
                        k.M.Sphere(b, 0.025f, 5, 3);
                        k.M.Emission = 0f; k.M.Color = belly;
                    }
                    // green runes on the flanks
                    k.M.Emission = 0.9f; k.M.Color = eyes; k.M.Bone = QB.Hips;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        k.M.Push().Translate(s * k.BodyR * 0.95f, k.HipY + 0.02f, k.Bind[QB.Hips].z + 0.05f).Rotate(0f, s * 90f, 0f);
                        k.M.Box(Vector3.zero, new Vector3(0.08f, 0.02f, 0.01f));
                        k.M.Box(new Vector3(0f, 0.035f, 0f), new Vector3(0.02f, 0.07f, 0.01f));
                        k.M.Pop();
                    }
                    k.M.Emission = 0f;
                    break;
                }
            }
            k.Finish(key, 1.0f, UnitStrike.Bite, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = tip - k.HeadBase;
            if (kind == WolfKind.Pet) m.Breath = 1.3f;
            return Bake(k);
        }

        // ================================================================== boars

        static UnitModel Boar(string key, bool pet)
        {
            var k = new QuadKit(pet ? 63 : 64, 0.48f, 0.52f, 0.6f, 0.27f, 0.13f, 0.055f, 0.36f, 0.38f, 0.02f, 0.16f, 0.17f, 0.03f);
            Color hide = pet ? C("#8a6248") : C("#6e4a32"), hideD = pet ? C("#6a4a36") : C("#4a3020"), snout = C("#c08070"),
                  tusk = C("#f2ead8"), mane = C("#2e2018"), eyes = C("#2a1a10");
            k.Body(hide, Paint.Shade(hide, 1.15f), 0.92f, 1.08f, 0.6f);
            k.Neck(hide, 1.15f, 1.1f);
            var tip = k.Head(hide, hideD, snout, eyes, 0.14f, 0.62f, false, 0.7f);
            k.M.Bone = QB.Head; k.M.Color = snout;
            k.M.Aim(tip + new Vector3(0f, 0f, -0.03f), Vector3.forward);
            k.M.Cylinder(Vector3.zero, 0.075f, 0.072f, 0.045f, 8);
            k.M.Pop();
            k.M.Color = Paint.Shade(snout, 0.5f);
            for (int s = -1; s <= 1; s += 2) k.M.Sphere(tip + new Vector3(s * 0.03f, 0.0f, 0.012f), 0.016f, 4, 2);
            k.Jaw(hideD, 0.16f, null, 0.42f);
            // curved tusks on the jaw
            k.M.Bone = QB.Jaw; k.M.Color = tusk;
            var j = k.Bind[QB.Jaw];
            for (int s = -1; s <= 1; s += 2)
                k.M.Curve(j + new Vector3(s * 0.06f, 0.0f, 0.13f), j + new Vector3(s * 0.11f, 0.03f, 0.2f), j + new Vector3(s * 0.08f, 0.12f, 0.18f), 0.018f, 0.004f, 3, 5);
            k.Ears(hide, hideD, 0.9f, 0.6f, 0.25f);
            k.Legs(hideD, hideD, C("#2a201a"), 1f, true);
            k.Tail(hide, 0.1f, 0.02f, mane, false, 1.2f);
            // spiky dark mane along the back
            k.M.Bone = QB.Back; k.M.Color = mane;
            for (int i = 0; i < 7; i++)
                k.M.Spike(k.Bind[QB.Back] + new Vector3(0f, -0.04f, 0.34f - i * 0.1f), new Vector3(0f, 1f, -0.6f), 0.035f, 0.13f - Mathf.Abs(i - 2f) * 0.012f, 4);
            if (pet)
            {
                // teal saddle blanket draped over the back (a cylinder section around the body's long axis)
                k.M.Bone = QB.Back;
                float axisY = (k.HipY + k.ChestY) * 0.5f;
                k.M.Push().Translate(0f, axisY, 0.02f).Rotate(90f, 0f, 0f);
                k.M.Panel(Vector3.zero, 100f, 260f, 7, new[] { new Vector2(k.BodyR * 1.12f, 0.17f), new Vector2(k.BodyR * 1.12f, -0.17f) }, 1f, C("#3f8f8a"), C("#2a5f5c"), 0.012f);
                k.M.Pop();
                k.M.Color = C("#e2b45a");
                k.M.Box(new Vector3(0f, axisY + k.BodyR * 1.13f, 0.02f), new Vector3(0.05f, 0.02f, 0.36f));
            }
            k.Finish(key, 0.9f, UnitStrike.Charge, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = tip - k.HeadBase;
            return Bake(k);
        }

        // ================================================================== cat (lynx), bear, sheep

        static UnitModel Cat(string key)
        {
            var k = new QuadKit(65, 0.3f, 0.32f, 0.36f, 0.11f, 0.06f, 0.028f, 0.28f, 0.3f, 0.1f, 0.14f, 0.1f, 0.025f);
            Color fur = C("#e0903a"), stripe = C("#a85a22"), cream = C("#f6e6c8"), eyes = C("#c8e060"), blue = C("#3f6fb8"), gold = C("#f0c840");
            k.Body(fur, cream);
            k.M.Color = stripe;
            for (int i = 0; i < 4; i++)
            {
                k.M.Bone = i < 2 ? QB.Chest : QB.Hips;
                float z = 0.12f - i * 0.11f;
                k.M.Push().Translate(0f, (i < 2 ? k.ChestY : k.HipY) + k.BodyR * 0.82f, z);
                k.M.Box(Vector3.zero, new Vector3(k.BodyR * 1.3f, 0.02f, 0.03f));
                k.M.Pop();
            }
            k.Neck(fur);
            var tip = k.Head(fur, cream, C("#d07a7a"), eyes, 0.05f, 0.55f, false, 1.15f);
            k.Jaw(cream, 0.07f, null, 0.3f);
            k.Ears(fur, cream, 1.2f, 0.6f, 0.05f, false, true);
            // cheek ruff
            k.M.Bone = QB.Head; k.M.Color = cream;
            for (int s = -1; s <= 1; s += 2)
                k.M.Spike(k.HeadBase + new Vector3(s * k.HeadR * 0.7f, -k.HeadR * 0.15f, k.HeadR * 0.4f), new Vector3(s, -0.5f, -0.2f), k.HeadR * 0.25f, k.HeadR * 0.55f, 4);
            k.Legs(fur, fur, cream);
            k.Tail(fur, 0.3f, 0.028f, C("#3a2a20"), false, -0.2f);
            // blue collar with a gold bell
            k.M.Bone = QB.Neck; k.M.Color = blue;
            var nb = Vector3.Lerp(k.NeckBase, k.HeadBase, 0.3f);
            k.M.Push().Translate(nb).Rotate(-45f, 0f, 0f);
            k.M.Torus(Vector3.zero, 0.055f, 0.012f, 10, 3);
            k.M.Pop();
            k.M.Color = gold; k.M.Emission = 0.2f;
            k.M.Sphere(nb + new Vector3(0f, -0.05f, 0.04f), 0.02f, 6, 4);
            k.M.Emission = 0f;
            k.Finish(key, 0.62f, UnitStrike.Bite, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = tip - k.HeadBase;
            m.MaxCadence = 4f;
            return Bake(k);
        }

        static UnitModel Bear(string key)
        {
            var k = new QuadKit(66, 0.82f, 0.9f, 0.8f, 0.38f, 0.2f, 0.1f, 0.72f, 0.74f, 0.08f, 0.22f, 0.22f, 0.04f);
            Color fur = C("#7a5232"), furD = C("#5a3a22"), muzzle = C("#b08a62"), eyes = C("#2a1a10"), teal = C("#3f8f8a");
            k.Body(fur, Paint.Shade(fur, 1.1f), 0.95f, 1.08f, 0.8f);
            k.Neck(fur, 1.1f, 1.1f);
            var tip = k.Head(fur, muzzle, C("#2a2020"), eyes, 0.12f, 0.6f, false, 0.8f);
            k.Jaw(muzzle, 0.14f, C("#f4f0e0"), 0.38f);
            k.Ears(fur, furD, 1f, 0.6f, 0.15f, true);
            k.Legs(fur, furD, furD, 1f);
            k.Tail(fur, 0.06f, 0.06f, null, false, 1f);
            k.M.Bone = QB.Neck; k.M.Color = teal;
            var nb = Vector3.Lerp(k.NeckBase, k.HeadBase, 0.4f);
            k.M.Push().Translate(nb).Rotate(-60f, 0f, 0f);
            k.M.Torus(Vector3.zero, 0.2f, 0.05f, 10, 4);
            k.M.Pop();
            k.M.Bone = QB.Chest;
            k.M.QuadTwoSided(nb + new Vector3(-0.1f, -0.1f, 0.12f), nb + new Vector3(0.1f, -0.1f, 0.12f), nb + new Vector3(0.02f, -0.32f, 0.16f), nb + new Vector3(-0.02f, -0.32f, 0.16f));
            k.Finish(key, 1.35f, UnitStrike.Swipe, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = tip - k.HeadBase;
            m.Heavy = 0.4f; m.MaxCadence = 2.4f; m.TurnRate = 360f;
            return Bake(k);
        }

        static UnitModel Sheep(string key)
        {
            var k = new QuadKit(67, 0.48f, 0.5f, 0.45f, 0.25f, 0.1f, 0.03f, 0.36f, 0.38f, 0.1f, 0.16f, 0.1f, 0.025f);
            Color wool = C("#f6f2ea"), woolD = C("#e2dccf"), face = C("#3a3436"), eyes = C("#2a2420");
            k.M.Jitter = 0.04f;
            k.M.Bone = QB.Hips; k.M.Color = wool;
            k.M.Blob(k.Bind[QB.Hips] + new Vector3(0f, 0.02f, 0.06f), new Vector3(0.27f, 0.25f, 0.28f), 1, 0.12f, 3);
            k.M.Bone = QB.Chest;
            k.M.Blob(k.Bind[QB.Chest] + new Vector3(0f, 0.02f, -0.08f), new Vector3(0.28f, 0.26f, 0.28f), 1, 0.12f, 5);
            k.M.Color = woolD;
            for (int i = 0; i < 4; i++)
                k.M.Blob(new Vector3((i % 2 == 0 ? -1 : 1) * 0.14f, 0.7f + (i / 2) * 0.03f, -0.15f + i * 0.1f), new Vector3(0.11f, 0.1f, 0.11f), 0, 0.2f, 9 + i);
            k.Neck(woolD, 0.8f, 0.8f);
            var tip = k.Head(face, face, C("#1a1618"), eyes, 0.06f, 0.6f);
            k.M.Bone = QB.Head; k.M.Color = wool;
            k.M.Blob(k.HeadBase + new Vector3(0f, k.HeadR * 0.9f, k.HeadR * 0.2f), new Vector3(0.09f, 0.06f, 0.09f), 0, 0.2f, 4);
            k.M.Color = face;
            for (int s = -1; s <= 1; s += 2)
                k.M.Spike(k.HeadBase + new Vector3(s * k.HeadR * 0.8f, k.HeadR * 0.4f, k.HeadR * 0.2f), new Vector3(s, -0.2f, -0.2f), 0.025f, 0.08f, 4);
            k.Jaw(face, 0.06f, null, 0.3f);
            k.Legs(face, face, C("#1a1618"), 1f, true);
            k.Tail(wool, 0.05f, 0.05f, null, true, 0.8f);
            k.Finish(key, 0.85f, UnitStrike.Bump, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = tip - k.HeadBase;
            m.Breath = 1.4f;
            return Bake(k);
        }

        // ================================================================== the Hollow Warden (boss stag, ~4.5 m)

        static UnitModel Warden(string key)
        {
            var k = new QuadKit(68, 2.2f, 2.4f, 1.9f, 0.55f, 0.32f, 0.11f, 2.15f, 2.15f, 0.7f, 0.95f, 0.3f, 0.06f);
            Color coat = C("#e8e4ec"), coatD = C("#b8b0c8"), ink = C("#4a3e5e"), inkD = C("#2e2640"), violet = C("#c9a8ff"),
                  cloth = C("#6a4a9a"), clothD = C("#44306a"), rope = C("#d8c080"), mane = C("#f6f4f0"), red = C("#d8402e"),
                  antler = C("#d8ccb8"), gold = C("#ffd27a"), paper = C("#f2e6c8");
            k.M.Jitter = 0.06f;
            k.Body(coat, coatD, 0.95f, 1.05f, 0.3f);
            // the Hollow creeping up: ink-violet belly patches and glowing veins
            k.M.Bone = QB.Hips; k.M.Color = ink;
            k.M.Sphere(k.Bind[QB.Hips] + new Vector3(0f, -0.25f, 0.1f), new Vector3(0.45f, 0.3f, 0.55f), 9, 5, false);
            k.M.Emission = 0.9f; k.M.Color = violet;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 3; i++)
                {
                    k.M.Push().Translate(s * 0.53f, k.HipY - 0.15f + i * 0.12f, k.Bind[QB.Hips].z + 0.2f - i * 0.25f).Rotate(i * 25f, 0f, s * 60f);
                    k.M.Box(Vector3.zero, new Vector3(0.03f, 0.3f, 0.04f));
                    k.M.Pop();
                }
            k.M.Emission = 0f;
            k.Neck(coat, 0.9f, 1.1f);
            // shaggy white throat mane
            k.M.Bone = QB.Neck; k.M.Color = mane;
            for (int i = 0; i < 4; i++)
                k.M.Blob(Vector3.Lerp(k.NeckBase, k.HeadBase, 0.2f + i * 0.2f) + new Vector3(0f, -0.18f, 0.12f), new Vector3(0.22f, 0.24f, 0.18f) * (1f - i * 0.12f), 0, 0.25f, 30 + i);
            var tip = k.Head(coat, coatD, C("#3a3040"), violet, 0.38f, 0.42f, true, 1.1f);
            k.Jaw(coatD, 0.36f, null, 0.3f);
            // vermilion spirit mask marking
            k.M.Bone = QB.Head; k.M.Color = red;
            var hc = k.HeadBase + new Vector3(0f, k.HeadR * 0.2f, k.HeadR * 0.35f);
            k.M.Box(hc + new Vector3(0f, k.HeadR * 0.75f, k.HeadR * 0.6f), new Vector3(0.05f, 0.18f, 0.03f));
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Push().Translate(hc + new Vector3(s * k.HeadR * 0.62f, k.HeadR * 0.38f, k.HeadR * 0.62f)).Rotate(0f, s * 35f, s * 30f);
                k.M.Box(Vector3.zero, new Vector3(0.16f, 0.035f, 0.03f));
                k.M.Pop();
            }
            k.Ears(coat, coatD, 1.1f, 0.85f, 0.2f);
            // vast branching antlers hung with paper lanterns (some gold, some guttering violet)
            k.M.Color = antler;
            for (int s = -1; s <= 1; s += 2)
            {
                var a0 = hc + new Vector3(s * k.HeadR * 0.45f, k.HeadR * 0.8f, -k.HeadR * 0.1f);
                var a1 = a0 + new Vector3(s * 0.35f, 0.45f, -0.1f);
                var a2 = a1 + new Vector3(s * 0.25f, 0.35f, -0.15f);
                var a3 = a2 + new Vector3(s * 0.1f, 0.3f, 0.05f);
                k.M.Segment(a0, a1, 0.06f, 0.045f, 6);
                k.M.Segment(a1, a2, 0.045f, 0.035f, 6);
                k.M.Segment(a2, a3, 0.035f, 0.012f, 5);
                var t1 = a1 + new Vector3(s * 0.35f, 0.1f, 0.15f);
                var t2 = a2 + new Vector3(s * 0.3f, 0.05f, 0.2f);
                var t3 = a1 + new Vector3(s * 0.05f, 0.35f, 0.25f);
                k.M.Segment(a1, t1, 0.035f, 0.01f, 5);
                k.M.Segment(a2, t2, 0.03f, 0.01f, 5);
                k.M.Segment(a1 + (a2 - a1) * 0.3f, t3, 0.03f, 0.01f, 5);
                k.M.Segment(a0 + (a1 - a0) * 0.4f, a0 + new Vector3(s * 0.12f, 0.25f, 0.25f), 0.03f, 0.01f, 5);
                // lanterns hanging from the tines
                var hangs = s < 0 ? new[] { t1, t2 } : new[] { t1, t3 };
                for (int i = 0; i < hangs.Length; i++)
                {
                    bool lit = (i + (s > 0 ? 1 : 0)) % 2 == 0;
                    var h = hangs[i];
                    k.M.Color = rope;
                    k.M.Segment(h, h + Vector3.down * 0.12f, 0.006f, 0.006f, 3);
                    k.M.Emission = lit ? 1f : 0.7f;
                    k.M.Color = lit ? gold : violet;
                    k.M.Sphere(h + Vector3.down * 0.2f, new Vector3(0.065f, 0.085f, 0.065f), 7, 5);
                    k.M.Emission = 0f;
                    k.M.Color = paper;
                    k.M.Cylinder(h + Vector3.down * 0.13f, 0.03f, 0.03f, 0.02f, 6);
                    k.M.Color = antler;
                }
            }
            k.Legs(coatD, ink, inkD, 1f, true);
            k.Tail(coat, 0.3f, 0.09f, mane, true, 0.9f);
            // tattered spirit cloth over the back + straw rope
            // (draped around the body's long axis: a cylinder section rotated so its axis runs along Z)
            k.M.Bone = QB.Back;
            float axisY = (k.HipY + k.ChestY) * 0.5f;
            float cr = k.BodyR * 1.12f;
            k.M.Push().Translate(0f, axisY, -0.1f).Rotate(90f, 0f, 0f);
            k.M.Panel(Vector3.zero, 75f, 285f, 10, new[] { new Vector2(cr, 0.55f), new Vector2(cr * 1.02f, 0f), new Vector2(cr, -0.6f) }, 1f, cloth, clothD, 0.015f);
            k.M.Pop();
            k.M.Color = clothD;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 4; i++)
                {
                    float z = -0.6f + i * 0.35f;
                    var a = new Vector3(s * cr * 0.966f, axisY - cr * 0.259f, z);
                    k.M.Blade(a, a + new Vector3(s * 0.03f, -0.28f - (i % 2) * 0.12f, 0.02f), 0.16f, Vector3.forward);
                }
            k.M.Color = rope;
            k.M.Push().Translate(0f, axisY, 0.42f).Rotate(90f, 0f, 0f);
            k.M.Torus(Vector3.zero, cr + 0.02f, 0.035f, 14, 4);
            k.M.Pop();
            for (int i = 0; i < 3; i++)
            {
                k.M.Color = paper;
                var a = new Vector3(cr * 0.9f, axisY + 0.25f - i * 0.18f, 0.44f);
                k.M.Blade(a, a + new Vector3(0.04f, -0.2f, 0.01f), 0.07f, Vector3.forward);
            }
            k.Finish(key, 4.5f, UnitStrike.Stomp, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = tip - k.HeadBase;
            m.HeadTop = hc + new Vector3(0f, 0.9f, 0f) - k.HeadBase;
            m.Heavy = 1f; m.MaxCadence = 1.0f; m.TurnRate = 110f; m.StrideK = 1.05f;
            m.DustColor = new Color(0.8f, 0.76f, 0.86f, 0.45f);
            return Bake(k);
        }

        // ================================================================== spider

        static UnitModel Spider(string key)
        {
            var k = new SpiderKit(69, 0.32f, 0.2f, 0.62f);
            k.Build(C("#4a3a30"), C("#5a4a3a"), C("#c8a060"), C("#3a2e26"), C("#c8a060"), C("#ff5040"), C("#7d9a55"));
            k.Finish(key, 0.62f);
            k.Model.DustColor = new Color(0.8f, 0.74f, 0.6f, 0.2f);
            return UnitModels.Bake(k.Model, k.M);
        }

        // ================================================================== owl (flier on the biped rig)

        static UnitModel Owl(string key)
        {
            var k = new BipedKit(70, 0.8f, 0.17f, 0.2f, 1f, false, 0.9f, 0.6f);
            Color brown = C("#8a6242"), brownD = C("#5e4030"), cream = C("#f2e6cc"), amber = C("#f0b030"), beak = C("#d8b060"), tuft = C("#4a3424");
            k.M.Jitter = 0.05f;
            // egg-shaped body
            k.M.Bone = BB.Hips; k.M.Color = brown;
            var bodyC = new Vector3(0f, 0.32f, 0f);
            k.M.Sphere(bodyC, new Vector3(0.2f, 0.26f, 0.19f), 10, 7);
            k.M.Bone = BB.Chest; k.M.Color = cream;
            k.M.Sphere(bodyC + new Vector3(0f, 0.02f, 0.06f), new Vector3(0.15f, 0.2f, 0.15f), 9, 6);
            k.M.Color = brownD;
            for (int i = 0; i < 3; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    k.M.Push().Translate(bodyC + new Vector3(s * 0.04f, 0.08f - i * 0.08f, 0.2f)).Rotate(0f, 0f, s * 30f);
                    k.M.Box(Vector3.zero, new Vector3(0.06f, 0.012f, 0.012f));
                    k.M.Pop();
                }
            // big round head, ear tufts, huge amber eyes, beak
            k.M.Bone = BB.Head; k.M.Color = brown;
            var hc = new Vector3(0f, k.HeadCY - 0.05f, 0.02f);
            k.M.Sphere(hc, new Vector3(k.R * 1.05f, k.R * 0.9f, k.R * 0.95f), 10, 7);
            k.M.Color = cream;
            k.M.Sphere(hc + new Vector3(0f, -0.01f, k.R * 0.45f), new Vector3(k.R * 0.85f, k.R * 0.6f, k.R * 0.55f), 9, 5);
            k.M.Jitter = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = hc + new Vector3(s * k.R * 0.4f, k.R * 0.05f, k.R * 0.9f);
                k.M.Emission = 0.35f; k.M.Color = amber;
                k.M.Sphere(e, new Vector3(0.26f, 0.26f, 0.1f) * k.R, 8, 4);
                k.M.Emission = 0f; k.M.Color = C("#1a1410");
                k.M.Sphere(e + new Vector3(0f, 0f, 0.06f * k.R), new Vector3(0.13f, 0.13f, 0.06f) * k.R, 6, 3);
                k.M.Emission = 0.6f; k.M.Color = Color.white;
                k.M.Sphere(e + new Vector3(s * 0.05f * k.R, 0.07f * k.R, 0.1f * k.R), 0.04f * k.R, 4, 2);
                k.M.Emission = 0f; k.M.Color = tuft;
                k.M.Spike(hc + new Vector3(s * k.R * 0.6f, k.R * 0.7f, 0f), new Vector3(s * 0.4f, 1f, -0.1f), k.R * 0.16f, k.R * 0.5f, 4);
            }
            k.M.Jitter = 0.05f;
            k.M.Color = beak;
            k.M.Spike(hc + new Vector3(0f, -k.R * 0.15f, k.R * 0.95f), new Vector3(0f, -0.8f, 0.6f), k.R * 0.1f, k.R * 0.3f, 4);
            // wings (WingL/WingR pivot at the shoulders), spread a little
            for (int s = -1; s <= 1; s += 2)
            {
                int wb = s < 0 ? BB.WingL : BB.WingR;
                k.Bind[wb] = bodyC + new Vector3(s * 0.15f, 0.12f, -0.02f);
                var w0 = k.Bind[wb];
                k.M.Bone = wb; k.M.Color = brown;
                k.M.QuadTwoSided(w0, w0 + new Vector3(s * 0.38f, 0.05f, -0.05f), w0 + new Vector3(s * 0.42f, -0.12f, -0.1f), w0 + new Vector3(s * 0.05f, -0.22f, -0.06f));
                k.M.Color = brownD;
                for (int f = 0; f < 4; f++)
                {
                    var b = w0 + new Vector3(s * (0.12f + f * 0.09f), -0.1f - f * 0.01f, -0.06f);
                    k.M.Blade(b, b + new Vector3(s * 0.04f, -0.14f, -0.03f), 0.06f, Vector3.forward);
                }
            }
            // tail fan, talons
            k.Bind[BB.Tail] = bodyC + new Vector3(0f, -0.18f, -0.14f);
            k.M.Bone = BB.Tail; k.M.Color = brownD;
            for (int i = -2; i <= 2; i++)
                k.M.Blade(k.Bind[BB.Tail], k.Bind[BB.Tail] + new Vector3(i * 0.04f, -0.12f, -0.14f), 0.05f, Vector3.right);
            k.M.Bone = BB.Hips; k.M.Color = beak;
            for (int s = -1; s <= 1; s += 2)
                for (int c = -1; c <= 1; c++)
                    k.M.Spike(bodyC + new Vector3(s * 0.07f, -0.24f, 0.03f), new Vector3(c * 0.3f, -0.6f, 1f), 0.012f, 0.06f, 3);
            var m = k.Model;
            m.FloatHeight = 0.85f;
            m.Wings = true; m.WingsFlap = true;
            m.Strike = UnitStrike.Claw; m.Ranged = UnitRanged.Point;
            m.Dust = false;
            k.Finish(key, UnitGait.Flier);
            m.Legs = new UnitLeg[0];
            m.Height = 0.8f;
            m.Radius = 0.3f;
            m.HipY = k.Bind[BB.Hips].y;
            m.CenterBone = BB.Hips; m.CenterOffset = bodyC - k.Bind[BB.Hips];
            m.HeadTop = hc + new Vector3(0f, k.R * 1.1f, 0f) - k.Bind[BB.Head];
            m.CastBone = BB.Head; m.CastOffset = hc + new Vector3(0f, 0f, k.R) - k.Bind[BB.Head];
            m.PickBones = new[] { BB.Hips, BB.Head, BB.WingL, BB.WingR };
            m.PickPad = 0.2f;
            return UnitModels.Bake(m, k.M);
        }

        // ================================================================== static units

        static UnitModel Dummy(string key)
        {
            var k = new StaticKit(71, 0.82f);
            Color wood = C("#8a6242"), woodD = C("#5e4030"), straw = C("#e8c870"), strawD = C("#c8a050"), burlap = C("#c8b088"),
                  rope = C("#c8402e"), white = C("#f2ead8");
            k.M.Bone = TB.Base; k.M.Color = woodD;
            k.M.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.6f, 0.08f, 0.1f));
            k.M.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.1f, 0.08f, 0.6f));
            k.M.Color = wood;
            k.M.Cylinder(Vector3.zero, 0.06f, 0.05f, 0.85f, 7);
            k.M.Bone = TB.Top;
            k.M.Cylinder(new Vector3(0f, 0.8f, 0f), 0.05f, 0.045f, 0.55f, 7);
            k.M.Aim(new Vector3(-0.42f, 1.18f, 0f), Vector3.right);
            k.M.Cylinder(Vector3.zero, 0.035f, 0.035f, 0.84f, 6);
            k.M.Pop();
            k.M.Color = straw;
            k.M.Blob(new Vector3(0f, 1.1f, 0f), new Vector3(0.22f, 0.3f, 0.17f), 1, 0.12f, 3);
            k.M.Color = strawD;
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Blob(new Vector3(s * 0.42f, 1.16f, 0f), new Vector3(0.07f, 0.06f, 0.06f), 0, 0.25f, 5 + s);
                k.M.Blade(new Vector3(s * 0.44f, 1.15f, 0f), new Vector3(s * 0.5f, 1.05f, 0.02f), 0.05f);
            }
            k.M.Color = rope;
            k.M.Band(new Vector3(0f, 0f, 0f), 0.2f, 0.2f, 1.0f, 1.04f, 8, 0.8f);
            k.M.Band(new Vector3(0f, 0f, 0f), 0.18f, 0.18f, 1.24f, 1.28f, 8, 0.8f);
            // target painted on the chest (facing +Z)
            k.M.Jitter = 0f;
            k.M.Push().Translate(0f, 1.12f, 0.165f).Rotate(90f, 0f, 0f);
            k.M.Color = white; k.M.Cylinder(Vector3.zero, 0.11f, 0.11f, 0.01f, 10);
            k.M.Color = rope; k.M.Cylinder(new Vector3(0f, 0.01f, 0f), 0.075f, 0.075f, 0.01f, 10);
            k.M.Color = white; k.M.Cylinder(new Vector3(0f, 0.02f, 0f), 0.035f, 0.035f, 0.01f, 8);
            k.M.Pop();
            k.M.Jitter = 0.06f;
            // burlap head with stitched X eyes
            k.M.Color = burlap;
            k.M.Sphere(new Vector3(0f, 1.47f, 0f), new Vector3(0.14f, 0.14f, 0.13f), 9, 6);
            k.M.Color = C("#3a2a20");
            for (int s = -1; s <= 1; s += 2)
                for (int d = -1; d <= 1; d += 2)
                {
                    k.M.Push().Translate(s * 0.05f, 1.49f, 0.125f).Rotate(0f, s * 15f, d * 45f);
                    k.M.Box(Vector3.zero, new Vector3(0.05f, 0.01f, 0.012f));
                    k.M.Pop();
                }
            k.M.Color = straw;
            k.M.Blade(new Vector3(0f, 1.58f, 0f), new Vector3(0.05f, 1.66f, -0.03f), 0.05f);
            k.M.Blade(new Vector3(0f, 1.58f, 0f), new Vector3(-0.06f, 1.64f, 0.02f), 0.05f);
            k.Finish(key, 1.6f, 0.3f, false);
            var m = k.Model;
            m.Strike = UnitStrike.Bump; m.Ranged = UnitRanged.Pulse;
            return UnitModels.Bake(m, k.M);
        }

        /// <summary>Carved totem pole: 0 earth, 1 fire, 2 water, 3 air.</summary>
        static UnitModel Totem(string key, int element)
        {
            float h = element == 1 ? 1.3f : 1.5f;
            var k = new StaticKit(72 + element, h * 0.72f);
            Color wood = element == 2 ? C("#8a98a8") : element == 3 ? C("#d8d0c0") : C("#8a6242");
            Color woodD = Paint.Shade(wood, 0.72f);
            Color accent = element == 0 ? C("#6fa35a") : element == 1 ? C("#e85a2a") : element == 2 ? C("#4a8ad0") : C("#8fd0f0");
            Color glow = element == 0 ? C("#b8f080") : element == 1 ? C("#ffb040") : element == 2 ? C("#9fe0ff") : C("#e8f8ff");
            float top = h * 0.72f;
            k.M.Bone = TB.Base; k.M.Color = woodD;
            k.M.Cylinder(Vector3.zero, 0.16f, 0.14f, 0.08f, 7);
            k.M.Color = wood;
            k.M.Lathe(new[] { new Vector2(0.11f, 0.05f), new Vector2(0.12f, top * 0.45f), new Vector2(0.105f, top * 0.5f), new Vector2(0.12f, top * 0.95f), new Vector2(0.1f, top) }, 7, false, true, true);
            // two carved faces
            for (int f = 0; f < 2; f++)
            {
                float y = top * (0.28f + f * 0.45f);
                k.M.Color = Paint.Shade(wood, 0.45f);
                for (int s = -1; s <= 1; s += 2) k.M.Box(new Vector3(s * 0.04f, y + 0.05f, 0.11f), new Vector3(0.05f, 0.03f, 0.03f));
                k.M.Box(new Vector3(0f, y - 0.05f, 0.11f), new Vector3(0.1f, 0.025f, 0.03f));
                k.M.Color = accent;
                k.M.Box(new Vector3(0f, y + 0.11f, 0.105f), new Vector3(0.16f, 0.02f, 0.03f));
                for (int s = -1; s <= 1; s += 2) k.M.Box(new Vector3(s * 0.1f, y, 0.06f), new Vector3(0.04f, 0.12f, 0.1f));
            }
            // element top (wobbles/pulses on the Top bone)
            k.M.Bone = TB.Top;
            var tp = new Vector3(0f, top, 0f);
            switch (element)
            {
                case 0:
                    k.M.Color = C("#8a8a90");
                    k.M.Blob(tp + new Vector3(0f, 0.1f, 0f), new Vector3(0.14f, 0.12f, 0.13f), 0, 0.25f, 4);
                    k.M.Color = C("#a8a8b0");
                    k.M.Spike(tp + new Vector3(0.06f, 0.12f, 0f), new Vector3(0.4f, 1f, 0f), 0.05f, 0.18f, 4);
                    k.M.Spike(tp + new Vector3(-0.06f, 0.12f, 0.03f), new Vector3(-0.3f, 1f, 0.2f), 0.045f, 0.22f, 4);
                    k.M.Color = accent;
                    k.M.Blade(tp + new Vector3(0f, 0.2f, 0f), tp + new Vector3(0.02f, 0.38f, -0.05f), 0.09f);
                    k.M.Emission = 0.9f; k.M.Color = glow;
                    k.M.Sphere(tp + new Vector3(0f, 0.12f, 0.12f), 0.035f, 5, 3);
                    break;
                case 1:
                    k.M.Color = woodD;
                    k.M.Lathe(new[] { new Vector2(0.08f, 0f), new Vector2(0.15f, 0.08f), new Vector2(0.14f, 0.1f) }, 8, false, true, false);
                    k.M.Emission = 1f;
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 1.26f;
                        k.M.Color = i % 2 == 0 ? accent : glow;
                        k.M.Blade(tp + new Vector3(Mathf.Cos(a) * 0.05f, 0.06f, Mathf.Sin(a) * 0.05f), tp + new Vector3(Mathf.Cos(a) * 0.03f, 0.3f + (i % 2) * 0.1f, Mathf.Sin(a) * 0.03f), 0.1f);
                    }
                    break;
                case 2:
                    k.M.Color = accent;
                    for (int s = -1; s <= 1; s += 2)
                        k.M.Curve(tp + new Vector3(s * 0.08f, 0f, 0f), tp + new Vector3(s * 0.2f, 0.12f, 0f), tp + new Vector3(s * 0.08f, 0.28f, 0f), 0.035f, 0.012f, 4, 5);
                    k.M.Emission = 1f; k.M.Color = glow;
                    k.M.Sphere(tp + new Vector3(0f, 0.16f, 0f), 0.1f, 9, 6);
                    break;
                default:
                    k.M.Color = C("#f6f4f0");
                    for (int s = -1; s <= 1; s += 2)
                        for (int f = 0; f < 3; f++)
                            k.M.Blade(tp + new Vector3(s * 0.05f, 0.1f, 0f), tp + new Vector3(s * (0.22f + f * 0.04f), 0.18f + f * 0.08f, -0.04f), 0.07f, Vector3.forward);
                    k.M.Emission = 1f; k.M.Color = glow;
                    k.M.Sphere(tp + new Vector3(0f, 0.16f, 0f), 0.08f, 8, 6);
                    break;
            }
            k.M.Emission = 0f;
            k.Finish(key, h, 0.22f, true);
            var m = k.Model;
            m.Strike = UnitStrike.Bump; m.Ranged = UnitRanged.Pulse;
            m.CastOffset = new Vector3(0f, 0.16f, 0f);
            return UnitModels.Bake(m, k.M);
        }

        /// <summary>Hunter trap: a glowing rune plate with iron teeth on the ground.</summary>
        static UnitModel Trap(string key)
        {
            var k = new StaticKit(76, 0.05f);
            Color iron = C("#5a5a62"), ironL = C("#8a8a92"), glow = C("#ffd27a");
            k.M.Bone = TB.Base; k.M.Color = iron;
            k.M.Cylinder(Vector3.zero, 0.36f, 0.34f, 0.04f, 12);
            k.M.Bone = TB.Top;
            k.M.Emission = 0.9f; k.M.Color = glow;
            k.M.Torus(new Vector3(0f, 0.045f, 0f), 0.24f, 0.012f, 14, 3);
            k.M.Torus(new Vector3(0f, 0.045f, 0f), 0.14f, 0.01f, 12, 3);
            k.M.Emission = 0f; k.M.Color = ironL;
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 0.2f;
                k.M.Spike(new Vector3(Mathf.Cos(a) * 0.31f, 0.03f, Mathf.Sin(a) * 0.31f), new Vector3(-Mathf.Cos(a) * 0.3f, 1f, -Mathf.Sin(a) * 0.3f), 0.025f, 0.08f, 3);
            }
            k.Finish(key, 0.25f, 0.4f, true);
            var m = k.Model;
            m.Strike = UnitStrike.Bump; m.Ranged = UnitRanged.Pulse;
            m.CastOffset = new Vector3(0f, 0.05f, 0f);
            m.HeadTop = new Vector3(0f, 0.2f, 0f);
            return UnitModels.Bake(m, k.M);
        }

        /// <summary>Lightwell: a stone tōrō lantern with glowing paper (priest summon).</summary>
        static UnitModel Lightwell(string key)
        {
            var k = new StaticKit(77, 1.2f);
            Color stone = C("#b8b4aa"), stoneD = C("#8a867e"), moss = C("#7d9a55"), paper = C("#ffe6a8");
            k.M.Bone = TB.Base; k.M.Color = stoneD;
            k.M.Cylinder(Vector3.zero, 0.36f, 0.32f, 0.14f, 6);
            k.M.Color = stone;
            k.M.Cylinder(new Vector3(0f, 0.14f, 0f), 0.13f, 0.11f, 0.85f, 6);
            k.M.Color = moss;
            k.M.Blob(new Vector3(0.2f, 0.14f, 0.1f), new Vector3(0.12f, 0.05f, 0.1f), 0, 0.3f, 3);
            k.M.Bone = TB.Top; k.M.Color = stone;
            k.M.Cylinder(new Vector3(0f, 0.99f, 0f), 0.26f, 0.24f, 0.12f, 6);
            k.M.Emission = 1f; k.M.Color = paper;
            k.M.Box(new Vector3(0f, 1.27f, 0f), new Vector3(0.3f, 0.3f, 0.3f));
            k.M.Emission = 0f; k.M.Color = stoneD;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    k.M.Box(new Vector3(sx * 0.16f, 1.27f, sz * 0.16f), new Vector3(0.05f, 0.32f, 0.05f));
            k.M.Color = stone;
            k.M.Lathe(new[] { new Vector2(0.38f, 1.43f), new Vector2(0.4f, 1.47f), new Vector2(0.12f, 1.66f), new Vector2(0f, 1.7f) }, 6, false, true, false, null, 30f);
            k.M.Sphere(new Vector3(0f, 1.78f, 0f), 0.07f, 6, 4, false);
            k.Finish(key, 1.85f, 0.4f, true);
            var m = k.Model;
            m.Strike = UnitStrike.Bump; m.Ranged = UnitRanged.Pulse;
            m.CastOffset = new Vector3(0f, 1.27f - 1.2f, 0f);
            return UnitModels.Bake(m, k.M);
        }
    }
}
