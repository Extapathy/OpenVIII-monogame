﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenVIII
{
    public static partial class Module_main_menu_debug
    {
        #region Classes

        private partial class IGM_Items
        {
            #region Classes

            /// <summary>
            /// </summary>
            /// <remarks>
            /// Using Faces.ID because it contains characters and gfs. Can cast to Characters or
            /// subtract 16 and cast to GFs
            /// </remarks>
            private class IGMData_TargetPool : IGMData_Pool<Saves.Data, Faces.ID>
            {
                #region Fields

                private bool eventSet;

                #endregion Fields

                #region Constructors

                public IGMData_TargetPool() : base(9, 3, new IGMDataItem_Box(pos: new Rectangle(420, 150, 420, 360), title: Icons.ID.TARGET), 9, 1) => Cursor_Status &= ~Cursor_Status.Enabled;

                #endregion Constructors

                #region Properties

                public Item_In_Menu Item { get; private set; }
                public byte TopMenuChoice { get; private set; }

                private bool All => (Item.Target & (Item_In_Menu._Target.All | Item_In_Menu._Target.All2)) != 0;

                private bool IsMe => InGameMenu_Items.GetMode().Equals(Mode.UseItemOnTarget);

                #endregion Properties

                #region Methods

                protected override void InitShift(int i, int col, int row)
                {
                    base.InitShift(i, col, row);
                    SIZE[i].Inflate(-18, -20);
                    SIZE[i].Y -= 3 * row;
                    //SIZE[i].X += 2;
                    SIZE[i].Height = (int)(12 * TextScale.Y);
                }

                public override void Draw()
                {
                    if (All && IsMe)
                        Cursor_Status &= ~Cursor_Status.Enabled;
                        base.Draw();
                    if (All && IsMe)
                    {
                        // if all draw blinking pointers on everyone.
                        byte i = 0;
                        foreach (Point c in CURSOR)
                        {
                            if (!BLANKS[i] && ITEM[i,0] != null && ITEM[i,0].Enabled && c != Point.Zero)
                                DrawPointer(c, blink: true);
                            i++;
                        }
                        Cursor_Status |= Cursor_Status.Enabled;
                    }
                }

                public override bool Inputs()
                {
                    // if ((Item.Target & (Item_In_Menu._Target.All|Item_In_Menu._Target.All2)) == 0)
                    Cursor_Status |= Cursor_Status.Enabled;
                    return base.Inputs();
                }

                public override void Inputs_CANCEL()
                {
                    base.Inputs_CANCEL();
                    InGameMenu_Items.SetMode(Mode.SelectItem);
                }

                public override void ReInit()
                {
                    if (!eventSet && InGameMenu_Items != null)
                    {
                        InGameMenu_Items.ModeChangeHandler += ModeChangeEvent;
                        InGameMenu_Items.ChoiceChangeHandler += ChoiceChangeEvent;
                        InGameMenu_Items.ItemChangeHandler += ItemTypeChangeEvent;
                        eventSet = true;
                    }
                    ////List won't populate unless theres a valid item set.
                    //if (Item.Type == Item_In_Menu._Type.None && Memory.State.Items != null)
                    //    Item = Memory.MItems[Memory.State.Items.FirstOrDefault(m => m.ID > 0 && m.QTY > 0).ID];
                    //Fill();
                    base.ReInit();
                }

                private void ChoiceChangeEvent(object sender, KeyValuePair<byte, FF8String> e) => TopMenuChoice = e.Key;

                private void ItemTypeChangeEvent(object sender, KeyValuePair<Item_In_Menu, FF8String> e)
                {
                    CURSOR_SELECT = 0;
                    if (!Item.Equals(e.Key) || Page > 0)
                    {
                        Page = 0;
                        bool sameTargets = Item.Target != e.Key.Target || Item.Type != e.Key.Type;
                        if(!sameTargets)
                        {
                            sameTargets = (Item.Type == Item_In_Menu._Type.GF_Learn && Item.Learn != e.Key.Learn);
                            sameTargets = sameTargets || (Item.Type == Item_In_Menu._Type.Blue_Magic && Item.Learned_Blue_Magic != e.Key.Learned_Blue_Magic);
                        }
                        Item = e.Key;
                        if (sameTargets)
                        {
                            Fill();
                            base.ReInit();
                        }
                    }
                }

                protected override void PAGE_NEXT()
                {
                    if (Pages > 1)
                    {
                        base.PAGE_NEXT();
                        Fill();
                        base.ReInit();
                    }
                }

                protected override void PAGE_PREV()
                {
                    if (Pages > 1)
                    {
                        base.PAGE_PREV();
                        Fill();
                        base.ReInit();
                    }
                }



                


                protected override void SetCursor_select(int value)
                {
                    if (!value.Equals(GetCursor_select()))
                    {
                        base.SetCursor_select(value);
                        InGameMenu_Items.TargetChangeHandler?.Invoke(this, Contents[CURSOR_SELECT]);
                    }
                }

                public override void ResetPages()
                {
                }

                private void Fill()
                {
                    Faces.ID id = 0;
                    int skip = Page * rows;
                    for (int i = 0; i < rows; i++)
                    {
                        bool gftest = false;
                        bool ctest = false;
                        Characters character = Characters.Blank;
                        GFs gf = GFs.Blank;
                        while (!Enum.IsDefined(typeof(Faces.ID), id)
                            || !((ctest = Item.TestCharacter(ref id, out character)) || (gftest = Item.TestGF(ref id, out gf)))
                            || skip-- > 0)
                            if ((byte)++id > 32)
                            {
                                for (; i < rows; i++)
                                {
                                    ITEM[i, 0] = null;
                                    ITEM[i, 1] = null;
                                    ITEM[i, 2] = null;
                                    BLANKS[i] = true;
                                    Contents[i] = Faces.ID.Blank;
                                }
                                Pages = Page + 1;
                                return;
                            }
                        ITEM[i, 0] = new IGMDataItem_String(Memory.Strings.GetName(id), pos: SIZE[i]);
                        int hp = ((ctest || gftest)? Memory.State.CurrentHP(character:character,gf:gf) : -1);
                        BLANKS[i] = false;
                        Contents[i] = id;
                        if (hp > -1)
                        {
                            ITEM[i, 1] = new IGMDataItem_Icon(Icons.ID.HP2, new Rectangle(SIZE[i].X + SIZE[i].Width - (20 * 7), SIZE[i].Y, 0, 0), 13);
                            ITEM[i, 2] = new IGMDataItem_Int(hp, pos: new Rectangle(SIZE[i].X + SIZE[i].Width - (20 * 4), SIZE[i].Y, 0, 0), spaces: 4);                            
                        }
                        else
                        {
                            ITEM[i, 1] = null;
                            ITEM[i, 2] = null;
                        }

                        id++;
                    }
                    Pages = Page + 2;
                }

                private void ModeChangeEvent(object sender, Mode e)
                {
                    if (!IsMe)
                        Cursor_Status &= ~Cursor_Status.Enabled;
                    else
                        InGameMenu_Items.TargetChangeHandler?.Invoke(this, Contents[CURSOR_SELECT]);
                }

                #endregion Methods
            }

            #endregion Classes
        }

        #endregion Classes
    }
}