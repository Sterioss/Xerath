#region

using System;
using System.Collections.Generic;
using System.Linq;
using HesaEngine.SDK;
using HesaEngine.SDK.Args;
using HesaEngine.SDK.Enums;
using HesaEngine.SDK.GameObjects;
using SharpDX;
using SharpDX.DirectInput;

#endregion

namespace Xerath
{
    internal class Program
    {
        public const string ChampionName = "Xerath";

        //Orbwalker instance
        public static Orbwalker.OrbwalkerInstance Orb;

        //Spells
        public static List<Spell> SpellList = new List<Spell>();

        public static Spell Q;
        public static Spell W;
        public static Spell E;
        public static Spell R;

        //Menu
        public static Menu Config;

        private static AIHeroClient _player;

        private static Vector2 _pingLocation;
        private static int _lastPingT = 0;
        private static bool AttacksEnabled
        {
            get
            {
                if (IsCastingR)
                {
                    return false;
                }


                if (!ObjectManager.Player.CanAttack)
                {
                    return false;
                }


                if (Config.Item("ComboActive").GetValue<KeyBind>().Active)
                {
                    return IsPassiveUp || (!Q.IsReady() && !W.IsReady() && !E.IsReady());
                }
                    

                return true;
            }
        }

        public static bool IsPassiveUp => ObjectManager.Player.HasBuff("xerathascended2onhit");

        public static bool IsCastingR => ObjectManager.Player.HasBuff("XerathLocusOfPower2") ||
                                         (ObjectManager.Player.LastCastedSpellName().Equals("XerathLocusOfPower2", StringComparison.InvariantCultureIgnoreCase) &&
                                          Utils.TickCount - ObjectManager.Player.LastCastedSpellT() < 500);

        public static class RCharge
        {
            public static int CastT;
            public static int Index;
            public static Vector3 Position;
            public static bool TapKeyPressed;
        }

        private static void Main(string[] args)
        {
            Game.OnGameLoaded += OnLoad;
        }

        private static void OnLoad()
        {
            _player= ObjectManager.Player;

            if (_player.ChampionName != ChampionName) return;

            //Create the spells
            Q = new Spell(SpellSlot.Q, 1550);
            W = new Spell(SpellSlot.W, 1000);
            E = new Spell(SpellSlot.E, 1150);
            R = new Spell(SpellSlot.R, 675);

            Q.SetSkillshot(0.6f, 95f, float.MaxValue, false, SkillshotType.SkillshotLine);
            W.SetSkillshot(0.7f, 125f, float.MaxValue, false, SkillshotType.SkillshotCircle);
            E.SetSkillshot(0.25f, 60f, 1400f, true, SkillshotType.SkillshotLine);
            R.SetSkillshot(0.7f, 130f, float.MaxValue, false, SkillshotType.SkillshotCircle);

            Q.SetCharged("XerathArcanopulseChargeUp", "XerathArcanopulseChargeUp", 750, 1550, 1.5f);

            SpellList.Add(Q);
            SpellList.Add(W);
            SpellList.Add(E);
            SpellList.Add(R);

            //Create the menu
            Config = Menu.AddMenu(ChampionName);

            //Orbwalker submenu
            Config.AddSubMenu("Orbwalking");

            //Add the target selector to the menu as submenu.

            TargetSelector.AddToMenu(Config);

            //Load the orbwalker and add it to the menu as submenu.
            Orb = new Orbwalker.OrbwalkerInstance(Config.SubMenu("Orbwalking"));

            //Combo menu:
            Config.AddSubMenu("Combo");
            Config.SubMenu("Combo").Add(new MenuCheckbox("UseQCombo", "Use Q").SetValue(true));
            Config.SubMenu("Combo").Add(new MenuCheckbox("UseWCombo", "Use W").SetValue(true));
            Config.SubMenu("Combo").Add(new MenuCheckbox("UseECombo", "Use E").SetValue(true));

            //Misc
            Config.AddSubMenu("R");
            Config.SubMenu("R").Add(new MenuCheckbox("EnableRUsage", "Auto use charges").SetValue(true));
            Config.SubMenu("R").Add(new MenuCombo("rMode", "Mode").SetValue(new StringList(new[] { "Normal", "Custom delays", "OnTap"}, 1)));
            Config.SubMenu("R").Add(new MenuKeybind("rModeKey", "OnTap key").SetValue(new KeyBind(Key.T, MenuKeybindType.Hold)));
            Config.SubMenu("R").AddSubMenu("Custom delays");
            for (var i = 1; i <= 5; i++)
                Config.SubMenu("R").SubMenu("Custom delays").Add(new MenuSlider("Delay"+i, "Delay"+i).SetValue(new Slider(0, 1500, 0)));
            Config.SubMenu("R").Add(new MenuCheckbox("PingRKillable", "Ping on killable targets (only local)").SetValue(true));
            Config.SubMenu("R").Add(new MenuCheckbox("BlockMovement", "Block right click while casting R").SetValue(false));
            /*Config.SubMenu("R").Add(new MenuCheckbox("OnlyNearMouse", "Focus only targets near mouse").SetValue(false));
            Config.SubMenu("R").Add(new MenuSlider("MRadius", "Radius").SetValue(new Slider(700, 1500, 300))); */

            //Harass menu:
            Config.AddSubMenu("Harass");
            Config.SubMenu("Harass").Add(new MenuCheckbox("UseQHarass", "Use Q").SetValue(true));
            Config.SubMenu("Harass").Add(new MenuCheckbox("UseWHarass", "Use W").SetValue(false));
            Config.SubMenu("Harass")
                .Add(new MenuKeybind("HarassToggle", "Harass (toggle)!").SetValue(new KeyBind(Key.T, MenuKeybindType.Toggle)));

            //Farming menu:
            Config.AddSubMenu("Farm");
            Config.SubMenu("Farm")
                .Add(
                    new MenuSlider("UseQFarm", "Use Q").SetValue(
                        new StringList(new[] { "Freeze", "LaneClear", "Both", "No" }, 2)));
            Config.SubMenu("Farm")
                .Add(
                    new MenuSlider("UseWFarm", "Use W").SetValue(
                        new StringList(new[] { "Freeze", "LaneClear", "Both", "No" }, 1)));
            Config.SubMenu("Farm")
                .Add(
                    new MenuKeybind("FreezeActive", "Freeze!").SetValue(
                        new KeyBind(Config.Item("Farm").GetValue<KeyBind>().Key, MenuKeybindType.Hold)));
            Config.SubMenu("Farm")
                .Add(
                    new MenuKeybind("LaneClearActive", "LaneClear!").SetValue(
                        new KeyBind(Config.Item("LaneClear").GetValue<KeyBind>().Key, MenuKeybindType.Hold)));

            //JungleFarm menu:
            Config.AddSubMenu("JungleFarm");
            Config.SubMenu("JungleFarm").Add(new MenuCheckbox("UseQJFarm", "Use Q").SetValue(true));
            Config.SubMenu("JungleFarm").Add(new MenuCheckbox("UseWJFarm", "Use W").SetValue(true));
            Config.SubMenu("JungleFarm")
                .Add(
                    new MenuKeybind("JungleFarmActive", "JungleFarm!").SetValue(
                        new KeyBind(Config.Item("LaneClear").GetValue<KeyBind>().Key, MenuKeybindType.Hold)));

            //Misc
            Config.AddSubMenu("Misc");
            Config.SubMenu("Misc").Add(new MenuCheckbox("InterruptSpells", "Interrupt spells").SetValue(true));
            Config.SubMenu("Misc").Add(new MenuCheckbox("AutoEGC", "AutoE gapclosers").SetValue(true));
            Config.SubMenu("Misc").Add(new MenuCheckbox("UseVHHC", "Use very high hit chance").SetValue(true));

            //Damage after combo:
            var dmgAfterComboItem = new MenuCheckbox("DamageAfterR", "Draw damage after (3 - 5)xR").SetValue(true);
            Utility.HpBarDamageIndicator.DamageToUnit += hero => (float)_player.GetSpellDamage(hero, SpellSlot.R) * new int[] {0, 3, 4, 5 }[_player.GetSpell(SpellSlot.R).Level];
            Utility.HpBarDamageIndicator.Enabled = dmgAfterComboItem.GetValue<bool>();
            dmgAfterComboItem.ValueChanged += delegate(object sender, OnValueChangeEventArgs eventArgs)
            {
                Utility.HpBarDamageIndicator.Enabled = eventArgs.GetNewValue<bool>();
            };

            //Drawings menu:
            Config.AddSubMenu("Drawings");
            Config.SubMenu("Drawings")
                .Add(
                    new MenuSlider("QRange", "Q range").SetValue(new Circle(true, ColorBGRA.FromRgba(150))));
            Config.SubMenu("Drawings")
                .Add(
                    new MenuSlider("WRange", "W range").SetValue(new Circle(true, ColorBGRA.FromRgba(150))));
            Config.SubMenu("Drawings")
                .Add(
                    new MenuSlider("ERange", "E range").SetValue(new Circle(false, ColorBGRA.FromRgba(150))));
            Config.SubMenu("Drawings")
                .Add(
                    new MenuSlider("RRange", "R range").SetValue(new Circle(false, ColorBGRA.FromRgba(150))));
            Config.SubMenu("Drawings")
                .Add(
                    new MenuSlider("RRangeM", "R range (minimap)").SetValue(new Circle(false,
                        ColorBGRA.FromRgba(150))));
            Config.SubMenu("Drawings")
                .Add(dmgAfterComboItem);

            //Add the events we are going to use:
            Game.OnUpdate += Game_OnGameUpdate;
            Drawing.OnDraw += Drawing_OnDraw;
            Drawing.OnEndScene += Drawing_OnEndScene;
            // AIHeroClient += Interrupter2_OnInterruptableTarget;
            AntiGapcloser.OnEnemyGapcloser += AntiGapcloser_OnEnemyGapcloser;
            AIHeroClient.OnProcessSpellCast += Obj_AI_Hero_OnProcessSpellCast;
            Game.OnWndProc += Game_OnWndProc;
            Orbwalker.BeforeAttack += OrbwalkingOnBeforeAttack;
            AIHeroClient.OnIssueOrder += Obj_AI_Hero_OnIssueOrder;
        }

      /*  static void Interrupter2_OnInterruptableTarget(AIHeroClient sender, Interrupter.InterruptableTargetEventArgs args)
        {
            if (!Config.Item("InterruptSpells").GetValue<bool>()) return;
                  
            if (_player.Distance(sender) < E.Range)
            {
                E.Cast(sender);
            }
        } */

        private static void Obj_AI_Hero_OnIssueOrder(Obj_AI_Base sender, GameObjectIssueOrderEventArgs args)
        {
            if (IsCastingR && Config.Item("BlockMovement").GetValue<bool>())
            {
                args.Process = false;
            }
        }

        private static void OrbwalkingOnBeforeAttack(Orbwalker.BeforeAttackEventArgs args)
        {
            args.Process = AttacksEnabled;
        }

        private static void AntiGapcloser_OnEnemyGapcloser(ActiveGapcloser gapcloser)
        {
            if (!Config.Item("AutoEGC").GetValue<bool>()) return;

            if (_player.Distance(gapcloser.Sender) < E.Range)
            {
                E.Cast(gapcloser.Sender);
            }
        }

        private static void Game_OnWndProc(WndEventArgs args)
        {
            if (args.Msg == 0x101)
                RCharge.TapKeyPressed = true;
        }

        private static void Obj_AI_Hero_OnProcessSpellCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
        {
            if (!sender.IsMe)
            {
                return;
            }

            if (args.SData.Name.Equals("XerathLocusOfPower2", StringComparison.InvariantCultureIgnoreCase))
            {
                RCharge.CastT = 0;
                RCharge.Index = 0;
                RCharge.Position = new Vector3();
                RCharge.TapKeyPressed = false;
            }
            else if (args.SData.Name.Equals("XerathLocusPulse", StringComparison.InvariantCultureIgnoreCase))
            {
                RCharge.CastT = Utils.TickCount;
                RCharge.Index++;
                RCharge.Position = args.End;
                RCharge.TapKeyPressed = false;
            }
        }

        private static void Combo()
        {
            UseSpells(Config.Item("UseQCombo").GetValue<bool>(), Config.Item("UseWCombo").GetValue<bool>(),
                Config.Item("UseECombo").GetValue<bool>());
        }

        private static void Harass()
        {
            UseSpells(Config.Item("UseQHarass").GetValue<bool>(), Config.Item("UseWHarass").GetValue<bool>(),
                false);
        }

        private static void UseSpells(bool useQ, bool useW, bool useE)
        {
            var qTarget = TargetSelector.GetTarget(Q.ChargedMaxRange, TargetSelector.DamageType.Magical);
            var wTarget = TargetSelector.GetTarget(W.Range + W.Width * 0.5f, TargetSelector.DamageType.Magical);
            var eTarget = TargetSelector.GetTarget(E.Range, TargetSelector.DamageType.Magical);

            // Hacks.DisableCastIndicator = Q.IsCharging && useQ;

            if (eTarget != null && useE && E.IsReady())
            {
                if (_player.Distance(eTarget) < E.Range * 0.4f)
                    E.Cast(eTarget);
                else if ((!useW || !W.IsReady()))
                    E.Cast(eTarget);
            }

            if (useQ && Q.IsReady() && qTarget != null)
            {
                if (Q.IsCharging)
                {
                    Q.Cast(qTarget, false, false);
                }
                else if (!useW || !W.IsReady() || _player.Distance(qTarget) > W.Range)
                {
                    Q.StartCharging();
                }
            }

            if (wTarget != null && useW && W.IsReady())
                W.Cast(wTarget, false, true);
        }

        private static AIHeroClient GetTargetNearMouse(float distance)
        {
            AIHeroClient bestTarget = null;
            var bestRatio = 0f;

            if (TargetSelector.SelectedTarget.IsValidTarget() && !TargetSelector.IsInvulnerable(TargetSelector.SelectedTarget, TargetSelector.DamageType.Magical, true) &&
                (Game.CursorPosition.Distance(TargetSelector.SelectedTarget.ServerPosition) < distance && ObjectManager.Player.Distance(TargetSelector.SelectedTarget) < R.Range))
            {
                return TargetSelector.SelectedTarget;
            }

            foreach (var hero in ObjectManager.Get<AIHeroClient>())
            {
                if (!hero.IsValidTarget(R.Range) || TargetSelector.IsInvulnerable(hero, TargetSelector.DamageType.Magical, true) || Game.CursorPosition.Distance(hero.ServerPosition) > distance)
                {
                    continue;
                }

                var damage = (float)ObjectManager.Player.CalcDamage(hero, Damage.DamageType.Magical, 100);
                var ratio = damage / (1 + hero.Health) * TargetSelector.GetPriority(hero);

                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    bestTarget = hero;
                }
            }

            return bestTarget;
        }

        private static void WhileCastingR()
        {
            if (!Config.Item("EnableRUsage").GetValue<bool>()) return;
            var rMode = Config.Item("rMode").GetValue<StringList>().GetHashCode();

            var rTarget = Config.Item("OnlyNearMouse").GetValue<bool>() ? GetTargetNearMouse(Config.Item("MRadius").GetValue<Slider>().CurrentValue) : TargetSelector.GetTarget(R.Range, TargetSelector.DamageType.Magical);

            if (rTarget != null)
            {
                //Wait at least 0.6f if the target is going to die or if the target is to far away
                if(rTarget.Health - R.GetDamage(rTarget) < 0)
                    if (Utils.TickCount - RCharge.CastT <= 700) return;

                if ((RCharge.Index != 0 && rTarget.Distance(RCharge.Position) > 1000))
                    if (Utils.TickCount - RCharge.CastT <= Math.Min(2500, rTarget.Distance(RCharge.Position) - 1000)) return;

                switch (rMode)
                {
                    case 0://Normal
                        R.Cast(rTarget, true);
                        break;

                    case 1://Selected delays.
                        var delay = Config.Item("Delay" + (RCharge.Index + 1)).GetValue<Slider>().CurrentValue;
                        if (Utils.TickCount - RCharge.CastT > delay)
                            R.Cast(rTarget, true);
                        break;

                    case 2://On tap
                        if (RCharge.TapKeyPressed)
                            R.Cast(rTarget, true);
                        break;
                }
            }
        }

        private static void Farm(bool laneClear)
        {
            var allMinionsQ = MinionManager.GetMinions(ObjectManager.Player.ServerPosition, Q.ChargedMaxRange,
                MinionTypes.All);
            var rangedMinionsW = MinionManager.GetMinions(ObjectManager.Player.ServerPosition, W.Range + W.Width + 30,
                MinionTypes.Ranged);

            var useQi = Config.Item("UseQFarm").GetValue<StringList>().GetHashCode();
            var useWi = Config.Item("UseWFarm").GetValue<StringList>().GetHashCode();
            var useQ = (laneClear && (useQi == 1 || useQi == 2)) || (!laneClear && (useQi == 0 || useQi == 2));
            var useW = (laneClear && (useWi == 1 || useWi == 2)) || (!laneClear && (useWi == 0 || useWi == 2));

            // Hacks.DisableCastIndicator = Q.IsCharging && useQi != 0;

            if (useW && W.IsReady())
            {
                var locW = W.GetCircularFarmLocation(rangedMinionsW, W.Width * 0.75f);
                if (locW.MinionsHit >= 3 && W.IsInRange(locW.Position.To3D()))
                {
                    W.Cast(locW.Position);
                    return;
                }
                else
                {
                    var locW2 = W.GetCircularFarmLocation(allMinionsQ, W.Width * 0.75f);
                    if (locW2.MinionsHit >= 1 && W.IsInRange(locW.Position.To3D()))
                    {
                        W.Cast(locW.Position);
                        return;
                    }
                        
                }
            }

            if (useQ && Q.IsReady())
            {
                if (Q.IsCharging)
                {
                    var locQ = Q.GetLineFarmLocation(allMinionsQ);
                    if (allMinionsQ.Count == allMinionsQ.Count(m => _player.Distance(m) < Q.Range) && locQ.MinionsHit > 0 && locQ.Position.IsValid())
                        Q.Cast(locQ.Position);
                }
                else if (allMinionsQ.Count > 0)
                    Q.StartCharging();
            }
        }

        private static void JungleFarm()
        {
            var useQ = Config.Item("UseQJFarm").GetValue<bool>();
            var useW = Config.Item("UseWJFarm").GetValue<bool>();
            var mobs = MinionManager.GetMinions(ObjectManager.Player.ServerPosition, W.Range, MinionTypes.All,
                MinionTeam.Neutral, MinionOrderTypes.MaxHealth);

            if (mobs.Count > 0)
            {
                var mob = mobs[0];
                if (useW && W.IsReady())
                {
                    W.Cast(mob);
                }
                else if (useQ && Q.IsReady())
                {
                    if (!Q.IsCharging)
                        Q.StartCharging();
                    else
                        Q.Cast(mob);
                }
            }
        }

        private static void Ping(Vector2 position)
        {
            if (Utils.TickCount - _lastPingT < 30 * 1000)
            {
                return;
            }
            
            _lastPingT = Utils.TickCount;
            _pingLocation = position;
            SimplePing();
            
            Utility.DelayAction(150, SimplePing);
            Utility.DelayAction(300, SimplePing);
            Utility.DelayAction(400, SimplePing);
            Utility.DelayAction(800, SimplePing);
        }

        private static void SimplePing()
        {
            TacticalMap.SendPing(PingCategory.Fallback, _pingLocation);
        }

        private static void Game_OnGameUpdate()
        {
            if (_player.IsDead) return;

            if (Config.SubMenu("Misc").Item("UseVHHC").GetValue<bool>())
            {
                Q.MinHitChance = HitChance.VeryHigh;
                W.MinHitChance = HitChance.VeryHigh;
                E.MinHitChance = HitChance.VeryHigh;
                R.MinHitChance = HitChance.VeryHigh;
            }
            else
            {
                Q.MinHitChance = HitChance.High;
                W.MinHitChance = HitChance.High;
                E.MinHitChance = HitChance.High;
                R.MinHitChance = HitChance.High;
            }
            Orbwalker.Move = true;

            //Update the R range
            R.Range = 1200 * R.Level + 2000; 

            if (IsCastingR)
            {
                Orbwalker.Move = false;
                WhileCastingR();
                return;
            }

            if (R.IsReady() && Config.Item("PingRKillable").GetValue<bool>())
            {
                foreach (var enemy in ObjectManager.Heroes.Enemies.Where(h => h.IsValidTarget() && (float)_player.GetSpellDamage(h, SpellSlot.R) * new int[] { 0, 3, 4, 5 }[_player.GetSpell(SpellSlot.R).Level] > h.Health))
                {
                    Ping(enemy.Position.To2D());
                }
            }

            if (Config.Item("ComboActive").GetValue<KeyBind>().Active)
            {
                Combo();
            }
            else
            {
                if (Config.Item("HarassActive").GetValue<KeyBind>().Active ||
                    Config.Item("HarassActiveT").GetValue<KeyBind>().Active)
                    Harass();

                var lc = Config.Item("LaneClearActive").GetValue<KeyBind>().Active;
                if (lc || Config.Item("FreezeActive").GetValue<KeyBind>().Active)
                    Farm(lc);

                if (Config.Item("JungleFarmActive").GetValue<KeyBind>().Active)
                    JungleFarm();
            }
        }

        private static void Drawing_OnEndScene(EventArgs args)
        {
            if (R.Level == 0) return;
            var menuCheckbox = Config.Item(R.Slot + "RangeM").GetValue<Circle>();
            if (menuCheckbox.Active)
                Drawing.DrawCircle(_player.Position, R.Range, menuCheckbox.Color, 1);
        }

        private static void Drawing_OnDraw(EventArgs args)
        {
            /*if (IsCastingR)
            {
                if (Config.Item("OnlyNearMouse").GetValue<bool>())
                {
                    Drawing.DrawCircle(Game.CursorPosition, Config.Item("MRadius").GetValue<Slider>().CurrentValue, Color.White);
                }
            }*/

            //Draw the ranges of the spells.
            foreach (var spell in SpellList)
            {
                var menuCheckbox = Config.Item(spell.Slot + "Range").GetValue<Circle>();
                if (menuCheckbox.Active && (spell.Slot != SpellSlot.R || R.Level > 0))
                    Drawing.DrawCircle(_player.Position, spell.Range, menuCheckbox.Color);
            }
        }
    }
}
