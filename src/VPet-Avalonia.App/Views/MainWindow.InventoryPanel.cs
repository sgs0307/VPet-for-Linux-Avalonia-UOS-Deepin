using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VPetAvalonia.Core;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 物品栏 (对应原版 WinDesign/winInventory) 的增强版。
    ///
    /// v1.2.2 的简版只有"分类过滤 + 使用 + 丢弃"。这一版补上原版里真正常用的几样:
    ///   · 名称搜索(和商店同一套输入)
    ///   · 排序: 数量 / 单价 / 名称 / 类型
    ///   · 收藏(星标): 收藏的东西永远排在最前, 落 favorites.txt
    ///   · 一次用多份: [用] 用 1 份, [全] 把这一叠全用掉(吃到饱就停, 不会白白浪费)
    ///   · 每行显示单价与效果摘要, 分类按钮上带件数
    /// 数据层仍是 Inventory(inventory.txt), 使用走 FeedFood, 与投喂菜单同一条路。
    /// </summary>
    public partial class MainWindow
    {
        private Border? _invPanel;
        private StackPanel? _invBody;       // 列表
        private StackPanel? _invTools;      // 固定工具栏
        private TextBox? _invSearch;
        private StackPanel? _invFilterRow;
        private StackPanel? _invSortRow;
        private TextBlock? _invHeadText;    // 头部副标题: "共 5 种 / 12 件 · 总价 $230"

        private Food.FoodType _invFilter = (Food.FoodType)(-1); // -1 = 全部
        private string _invQuery = "";
        private int _invSort = 0;                                // 0 数量 1 单价 2 名称 3 类型

        private static readonly (string Label, Food.FoodType? Type)[] InvFilters =
        {
            ("全部", null),
            ("正餐", Food.FoodType.Meal),
            ("饮料", Food.FoodType.Drink),
            ("零食", Food.FoodType.Snack),
            ("药品", Food.FoodType.Drug),
            ("礼品", Food.FoodType.Gift),
        };

        private static readonly string[] InvSortNames = { "数量", "单价", "名称", "类型" };

        /// <summary>当前物品行的展示顺序, 自检用它验证排序/收藏置顶</summary>
        internal List<string> LastInventoryOrder { get; private set; } = new();

        private void ShowInventoryPanel()
        {
            if (_invPanel == null)
            {
                _invPanel = CreatePanel("物品栏", 440, 600, out _invBody, out _invTools, out _, out _invHeadText, 92, 0);
                BuildInvTools();
                PetCanvas.Children.Add(_invPanel);
            }
            ShowOverlay(_invPanel, RefreshInventoryPanel);
        }

        private void BuildInvTools()
        {
            if (_invTools == null)
                return;

            _invSearch = new TextBox();
            _invSearch.TextChanged += (s, e) =>
            {
                var q = _invSearch?.Text ?? "";
                if (q == _invQuery)
                    return;
                _invQuery = q;
                RefreshInventoryList();
                UpdateClickThrough();
            };
            _invTools.Children.Add(UiSearch(_invSearch, "搜索名字 / 类型 / 效果, 或输入价格上限"));

            _invFilterRow = new StackPanel { Orientation = Orientation.Horizontal };
            _invTools.Children.Add(_invFilterRow);

            _invSortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            _invTools.Children.Add(_invSortRow);

            BuildInvFilterRow();
            BuildInvSortRow();
        }

        private void BuildInvFilterRow()
        {
            if (_invFilterRow == null)
                return;
            _invFilterRow.Children.Clear();

            var core = MainDisplay.Core;
            var foods = core?.Foods ?? new List<Food>();
            var inv = core?.Inventory;

            foreach (var f in InvFilters)
            {
                var type = f.Type;
                var count = inv == null
                    ? 0
                    : inv.Items.Where(kv => kv.Value > 0)
                        .Select(kv => foods.FirstOrDefault(x => x.Name == kv.Key))
                        .Where(x => x != null && (type == null || x.Type == type))
                        .Sum(x => inv.Count(x!.Name));

                var active = IsInvFilterActive(f);
                var pill = UiPill(count > 0 ? $"{f.Label} {count}" : f.Label, active, () =>
                {
                    _invFilter = type ?? (Food.FoodType)(-1);
                    BuildInvFilterRow();
                    RefreshInventoryList();
                    UpdateClickThrough();
                }, small: true);
                _invFilterRow.Children.Add(pill);
            }
        }

        private void BuildInvSortRow()
        {
            if (_invSortRow == null)
                return;
            _invSortRow.Children.Clear();

            _invSortRow.Children.Add(new TextBlock
            {
                Text = "排序",
                Foreground = UiFaint,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });

            for (int i = 0; i < InvSortNames.Length; i++)
            {
                var idx = i;
                var pill = UiPill(InvSortNames[i], _invSort == i, () =>
                {
                    _invSort = idx;
                    BuildInvSortRow();
                    RefreshInventoryList();
                    UpdateClickThrough();
                }, small: true);
                _invSortRow.Children.Add(pill);
            }

            var starCount = Favorites.Count;
            _invSortRow.Children.Add(new TextBlock
            {
                Text = starCount > 0 ? $"★ 已收藏 {starCount}" : "点 ☆ 收藏",
                Foreground = UiFaint,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            });
        }

        private void RefreshInventoryPanel()
        {
            BuildInvFilterRow();
            BuildInvSortRow();
            RefreshInventoryList();
        }

        private void RefreshInventoryList()
        {
            var core = MainDisplay.Core;
            var inv = core?.Inventory;
            if (_invBody == null || inv == null)
                return;

            _invBody.Children.Clear();

            var foods = core!.Foods;
            var entries = inv.Items
                .Where(kv => kv.Value > 0)
                .Select(kv => (food: foods.FirstOrDefault(f => f.Name == kv.Key), count: kv.Value))
                .Where(x => x.food != null)
                .ToList();

            // 收藏项已经不在包里了, 顺手清掉, 免得收藏数虚高
            Favorites.Prune(entries.Select(x => x.food!.Name));

            // 分类过滤
            if (_invFilter >= 0)
                entries = entries.Where(x => x.food!.Type == _invFilter).ToList();

            // 搜索(纯数字按单价上限过滤, 与商店一致)
            var q = _invQuery.Trim();
            if (q.Length > 0)
            {
                if (double.TryParse(q, out var maxPrice))
                    entries = entries.Where(x => x.food!.Price <= maxPrice).ToList();
                else
                    entries = entries.Where(x =>
                        x.food!.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.food.TypeName.Contains(q) ||
                        x.food.EffectText.Contains(q)).ToList();
            }

            // 排序(收藏永远在前)
            entries = _invSort switch
            {
                1 => entries.OrderByDescending(x => x.food!.Price).ToList(),
                2 => entries.OrderBy(x => x.food!.Name, StringComparer.Ordinal).ToList(),
                3 => entries.OrderBy(x => x.food!.Type).ThenBy(x => x.food!.Name, StringComparer.Ordinal).ToList(),
                _ => entries.OrderByDescending(x => x.count).ToList(),
            };
            entries = entries.OrderByDescending(x => Favorites.Contains(x.food!.Name)).ToList();
            LastInventoryOrder = entries.Select(x => x.food!.Name).ToList();

            var totalValue = entries.Sum(x => x.food!.Price * x.count);
            if (_invHeadText != null)
            {
                var narrowed = q.Length > 0 || _invFilter >= 0;
                _invHeadText.Text = $"共 {entries.Count} 种 / {entries.Sum(x => x.count)} 件 · 总价值 $ {totalValue:0.#}" +
                                    (narrowed ? $" (背包 {inv.Items.Count(kv => kv.Value > 0)} 种)" : "");
            }

            if (entries.Count == 0)
            {
                _invBody.Children.Add(MakeNote(q.Length > 0 || _invFilter >= 0
                    ? "这个条件下什么都没有, 换个筛选试试。"
                    : "背包空空的, 去商店买点东西吧~"));
                return;
            }

            foreach (var (food, count) in entries)
                _invBody.Children.Add(BuildInvRow(food!, count));

            _invBody.Children.Add(MakeNote("「用」吃 1 份, 「全」按数量连续吃(吃饱/喝够就停); 和投喂菜单同一条路, 都会播吃东西动画。"));
        }

        private Border BuildInvRow(Food food, int count)
        {
            var fav = Favorites.Contains(food.Name);

            // ---- 星标 ----
            var starText = new TextBlock
            {
                Text = fav ? "★" : "☆",
                Foreground = fav ? UiGold : Brush("#777777"),
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var star = new Border
            {
                Width = 24,
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                Child = starText,
            };
            star.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                Favorites.Toggle(food.Name);
                RefreshInventoryList();
            };

            // ---- 名称 + 副行 ----
            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            nameLine.Children.Add(new TextBlock
            {
                Text = food.Name,
                Foreground = Brushes.White,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            });
            nameLine.Children.Add(UiBadge($"×{count}", "#33FFFFFF"));

            var metaLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            metaLine.Children.Add(new TextBlock
            {
                Text = food.TypeName,
                Foreground = FoodTypeBrush(food.Type),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            metaLine.Children.Add(new TextBlock
            {
                Text = $" · $ {food.Price:0.#} · {food.EffectText}",
                Foreground = UiMuted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(nameLine);
            left.Children.Add(metaLine);

            // ---- 按钮: 用 / 全 / 弃 ----
            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };

            var useBtn = UiAction("用", BtnGreen, BtnGreenHover, () => UseOne(food.Name), noMargin: true);
            btns.Children.Add(useBtn);

            if (count > 1)
                btns.Children.Add(UiAction("全", BtnGreenDeep, BtnGreenDeepHover, () => UseAll(food)));

            btns.Children.Add(UiAction("弃", BtnGray, BtnGrayHover, () => DropOne(food.Name)));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.Children.Add(star);
            Grid.SetColumn(left, 1);
            grid.Children.Add(left);
            Grid.SetColumn(btns, 2);
            grid.Children.Add(btns);

            var card = UiCard(grid, 8, 6, 4, fav ? Brush("#1AFFD54F") : null);
            UiHover(card, fav ? Brush("#1AFFD54F") : null);
            return card;
        }

        private bool IsInvFilterActive((string Label, Food.FoodType? Type) f)
            => f.Type == null ? _invFilter == (Food.FoodType)(-1) : _invFilter == f.Type;

        // ---------------- 使用 / 丢弃 ----------------

        private void UseOne(string name)
        {
            FeedFood(name);
            RefreshInventoryPanel();
        }

        /// <summary>
        /// 连续使用: 一次吃多份。实际吃几份由 EatFood 决定(满了会提前停),
        /// 剩下的原样退回背包, 不会白白消耗。
        /// </summary>
        private void UseAll(Food food)
        {
            var inv = MainDisplay.Core?.Inventory;
            if (inv == null)
                return;

            // 一次最多连用 10 份: 再多也就吃那麼多, 一口气清空反而容易误点
            var want = Math.Min(inv.Count(food.Name), 10);
            if (want <= 0)
            {
                RefreshInventoryPanel();
                return;
            }

            // 先从背包取出最多 want 份, 吃不完的再退回去
            var taken = 0;
            while (taken < want && inv.TryConsume(food.Name))
                taken++;
            if (taken == 0)
            {
                RefreshInventoryPanel();
                return;
            }

            var ok = MainDisplay.EatFood(food, taken);
            var used = ok ? Math.Max(1, MainDisplay.LastEatUsed) : 0;
            if (used < taken)
                inv.Add(food.Name, taken - used);
            inv.Save();

            if (!ok)
                MainDisplay.ShowBubble($"「{food.Name}」现在用不上了(已经满了)");

            Console.WriteLine($"[VPet] 连续使用: {food.Name} ×{used}/{taken} 剩余 {inv.Count(food.Name)}");
            RefreshInventoryPanel();
            CheckAchievementsAndNotify();
        }

        private void DropOne(string name)
        {
            if (MainDisplay.Core?.Inventory?.TryConsume(name) == true)
            {
                MainDisplay.Core.Inventory.Save();
                MainDisplay.ShowBubble($"丢掉了「{name}」");
                Console.WriteLine($"[VPet] 丢弃: {name}");
            }
            RefreshInventoryPanel();
        }
    }
}
