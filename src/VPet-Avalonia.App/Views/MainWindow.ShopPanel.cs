using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using VPetAvalonia.Core;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 商店 (对应原版 WinDesign/winBetterBuy)。
    ///
    /// v1.1.0 的简版就是"122 种食物平铺成一张长列表, 点一下买一个"。原版有分类/搜索/
    /// 数量选择, 列表一长就不好找东西。这一版补齐:
    ///   · 分类筛选(带数量角标) + 名称搜索
    ///   · 排序: 默认(类型+价格) / 价格 / 名称 / 效果(饱腹+口渴性价比)
    ///   · 批量购买 ×1 / ×5 / ×10 (钱不够就按买得起的最大件数买, 并说明只买到几件)
    ///   · 买不起的条目压暗, 余额实时显示
    ///
    /// 与其它面板一致: 不是真窗口, 是 PetCanvas 里的 Border, 由 VisibleOverlays()
    /// 收进 XShape 输入区域。搜索框用的 TextBox 需要 FluentTheme 的控件模板,
    /// 本项目在 Program.cs 里手动 Styles.Add(new FluentTheme()) 挂上了。
    /// </summary>
    public partial class MainWindow
    {
        private Border? _shopPanel;
        private StackPanel? _shopBody;      // 列表区(可滚动)
        private StackPanel? _shopTools;     // 固定工具栏(搜索/分类/排序)
        private StackPanel? _shopFooter;    // 固定底部(翻页条)
        private TextBox? _shopSearch;
        private StackPanel? _shopFilterRow;
        private StackPanel? _shopSortRow;
        private TextBlock? _shopHeadText;   // "$ 123  共 N 项"
        private int _shopShownCount;

        private string _shopQuery = "";
        private Food.FoodType _shopFilter = (Food.FoodType)(-1);   // -1 = 全部
        private int _shopSort = 0;                                  // 0 默认 1 价格 2 名称 3 性价比
        private bool _shopOnlyAffordable;
        private int _shopPage;

        /// <summary>
        /// 每页条数。v1.1.0 是把 122 种食物一次性铺进面板(244 个控件)还能跑,
        /// 现在每行多了效果摘要和三个购买按钮(约 9 个控件/行), 一次性渲染 122 行 = 1100+ 控件,
        /// 在内存紧张的机器上直接把进程推到 OOM(实测 Aborted)。分页后峰值只有一页的控件量。
        /// </summary>
        private const int ShopPageSize = 12;

        /// <summary>可见行的轻量刷新动作: 买完不重建整张列表, 只改"已有 N / 买得起"状态, 保住滚动位置</summary>
        private readonly List<Action> _shopRowRefresh = new();

        private static readonly string[] ShopSortNames = { "默认", "价格", "名称", "性价比" };

        private void ShowShop()
        {
            var core = MainDisplay.Core;
            if (core == null)
                return;
            if (core.Foods.Count == 0)
            {
                MainDisplay.ShowBubble("没有加载到食物数据 (mod/*/food/*.lps)");
                return;
            }

            if (_shopPanel == null)
            {
                // 列表区用 CreateOverlayPanel 的 body, 工具栏挂在标题下面(不随列表滚走)
                _shopPanel = CreateOverlayPanel("商店", 430, 600, out _shopBody, out _shopTools, out _shopFooter, 84, 46);
                BuildShopTools();
                PetCanvas.Children.Add(_shopPanel);
            }
            ShowOverlay(_shopPanel, RefreshShop);
        }

        /// <summary>
        /// 工具栏: 搜索框 + 分类 + 排序 (只建一次, 状态变化时单独重建里面的按钮行)
        /// </summary>
        private void BuildShopTools()
        {
            if (_shopTools == null)
                return;

            _shopSearch = new TextBox
            {
                Watermark = "搜索名字 / 类型, 或输入价格上限(如 50)",
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 6),
                MinHeight = 28,
            };
            _shopSearch.TextChanged += (s, e) =>
            {
                var q = _shopSearch?.Text ?? "";
                if (q == _shopQuery)
                    return;
                _shopQuery = q;
                _shopPage = 0;
                RefreshShopList();
                UpdateClickThrough();
            };
            _shopTools.Children.Add(_shopSearch);

            _shopFilterRow = new StackPanel { Orientation = Orientation.Horizontal };
            _shopTools.Children.Add(_shopFilterRow);

            _shopSortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            _shopTools.Children.Add(_shopSortRow);

            BuildShopFilterRow();
            BuildShopSortRow();
        }

        private void BuildShopFilterRow()
        {
            if (_shopFilterRow == null)
                return;
            _shopFilterRow.Children.Clear();

            var foods = MainDisplay.Core?.Foods ?? new List<Food>();
            var inv = MainDisplay.Core?.Inventory;

            void AddPill(string label, Food.FoodType? type, int badge)
            {
                var active = type == null ? _shopFilter == (Food.FoodType)(-1) : _shopFilter == type;
                var btn = new Button
                {
                    Content = badge > 0 ? $"{label} {badge}" : label,
                    FontSize = 11,
                    Padding = new Thickness(7, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = active ? Brush("#AA0286C6") : Brush("#AA444444"),
                    Foreground = Brushes.White,
                };
                btn.Click += (s, e) =>
                {
                    _shopFilter = type ?? (Food.FoodType)(-1);
                    _shopPage = 0;
                    BuildShopFilterRow();
                    RefreshShopList();
                    UpdateClickThrough();
                };
                _shopFilterRow!.Children.Add(btn);
            }

            AddPill("全部", null, 0);
            foreach (var (label, t) in ShopFilters)
            {
                var n = foods.Count(f => f.Type == t);
                AddPill(label, t, n);
            }
        }

        private void BuildShopSortRow()
        {
            if (_shopSortRow == null)
                return;
            _shopSortRow.Children.Clear();

            _shopSortRow.Children.Add(new TextBlock
            {
                Text = "排序",
                Foreground = TextDim,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });

            for (int i = 0; i < ShopSortNames.Length; i++)
            {
                var idx = i;
                var btn = new Button
                {
                    Content = ShopSortNames[i],
                    FontSize = 11,
                    Padding = new Thickness(8, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = _shopSort == i ? Brush("#AA2E7D32") : Brush("#AA444444"),
                    Foreground = Brushes.White,
                };
                btn.Click += (s, e) =>
                {
                    _shopSort = idx;
                    _shopPage = 0;
                    BuildShopSortRow();
                    RefreshShopList();
                    UpdateClickThrough();
                };
                _shopSortRow.Children.Add(btn);
            }

            var affordable = MakeToggle(() => _shopOnlyAffordable, v =>
            {
                _shopOnlyAffordable = v;
                _shopPage = 0;
                RefreshShopList();
                UpdateClickThrough();
            }, "只看买得起", "全部商品");
            affordable.Margin = new Thickness(6, 0, 0, 0);
            _shopSortRow.Children.Add(affordable);
        }

        /// <summary>分类: 与物品栏保持一致, 只列原版常用的五类</summary>
        private static readonly (string Label, Food.FoodType Type)[] ShopFilters =
        {
            ("正餐", Food.FoodType.Meal),
            ("饮料", Food.FoodType.Drink),
            ("零食", Food.FoodType.Snack),
            ("药品", Food.FoodType.Drug),
            ("礼品", Food.FoodType.Gift),
        };

        /// <summary>
        /// 整体刷新: 工具栏角标 + 列表
        /// </summary>
        private void RefreshShop()
        {
            BuildShopFilterRow();
            BuildShopSortRow();
            RefreshShopList();
        }

        /// <summary>当前列表的展示顺序(食物名), 自检用它验证排序/筛选真的生效</summary>
        internal List<string> LastShopOrder { get; private set; } = new();

        /// <summary>
        /// 筛选 + 排序(纯逻辑, 抽出来给自检直接调用)
        /// </summary>
        internal static List<Food> SelectFoods(IEnumerable<Food> foods, Food.FoodType filter,
                                               string query, bool onlyAffordable, int sort, double money)
        {
            var list = foods;

            if (filter >= 0)
                list = list.Where(f => f.Type == filter);

            var q = (query ?? "").Trim();
            if (q.Length > 0)
            {
                // 纯数字按"价格上限"过滤: 桌宠跑在 X11 上, 中文输入要靠用户自己的输入法,
                // 万一 IME 用不了也还能输入数字/价格快速筛出便宜货
                if (double.TryParse(q, out var maxPrice))
                    list = list.Where(f => f.Price <= maxPrice);
                else
                    list = list.Where(f => f.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                                           || f.TypeName.Contains(q));
            }

            if (onlyAffordable)
                list = list.Where(f => f.Price <= money);

            var filtered = list.ToList();
            return sort switch
            {
                1 => filtered.OrderBy(f => f.Price).ThenBy(f => f.Name, StringComparer.Ordinal).ToList(),
                2 => filtered.OrderBy(f => f.Name, StringComparer.Ordinal).ToList(),
                3 => filtered.OrderByDescending(f => ValueScore(f)).ToList(),
                _ => filtered.ToList(),
            };
        }

        /// <summary>
        /// 按当前的筛选/搜索/排序重建列表
        /// </summary>
        private void RefreshShopList()
        {
            var core = MainDisplay.Core;
            if (core == null || _shopBody == null)
                return;

            _shopBody.Children.Clear();
            _shopRowRefresh.Clear();

            var money = core.Save?.Money ?? 0;
            var filtered = SelectFoods(core.Foods, _shopFilter, _shopQuery, _shopOnlyAffordable, _shopSort, money);
            LastShopOrder = filtered.Select(f => f.Name).ToList();

            _shopShownCount = filtered.Count;
            _shopHeadText = new TextBlock
            {
                Foreground = Brushes.Gold,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 4),
            };
            _shopBody.Children.Add(_shopHeadText);
            UpdateShopHeader();
            if (filtered.Count == 0)
            {
                _shopFooter?.Children.Clear();
                _shopBody.Children.Add(MakeNote(_shopOnlyAffordable
                    ? "按当前余额没有买得起的东西, 先去干活赚点钱吧~"
                    : "没有符合条件的东西, 换个筛选或清空搜索试试。"));
                return;
            }

            var pages = Math.Max(1, (filtered.Count + ShopPageSize - 1) / ShopPageSize);
            if (_shopPage >= pages)
                _shopPage = pages - 1;
            if (_shopPage < 0)
                _shopPage = 0;
            var page = _shopPage;
            var pageItems = filtered.Skip(page * ShopPageSize).Take(ShopPageSize).ToList();

            foreach (var food in pageItems)
                _shopBody.Children.Add(BuildShopRow(food));

            // ---- 翻页(放在固定底部, 否则会随列表滚出视野) ----
            BuildShopPager(pages, page);
        }

        /// <summary>
        /// 底部翻页条: 上一页 / 第 n/m 页 / 下一页
        /// </summary>
        private void BuildShopPager(int pages, int page)
        {
            if (_shopFooter == null)
                return;
            _shopFooter.Children.Clear();
            if (pages <= 1)
                return;

            var pager = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            Button PageBtn(string caption, bool enabled)
            {
                return new Button
                {
                    Content = caption,
                    FontSize = 12,
                    Padding = new Thickness(12, 3),
                    Margin = new Thickness(0, 0, 6, 0),
                    IsEnabled = enabled,
                    Background = Brush(enabled ? "#AA444444" : "#66333333"),
                    Foreground = Brushes.White,
                };
            }

            var prev = PageBtn("上一页", page > 0);
            prev.Click += (s, e) =>
            {
                if (_shopPage > 0) _shopPage--;
                RefreshShopList();
                UpdateClickThrough();
            };
            pager.Children.Add(prev);

            pager.Children.Add(new TextBlock
            {
                Text = $"{page + 1} / {pages}",
                Foreground = TextDim,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });

            var next = PageBtn("下一页", page < pages - 1);
            next.Click += (s, e) =>
            {
                if (_shopPage < pages - 1) _shopPage++;
                RefreshShopList();
                UpdateClickThrough();
            };
            pager.Children.Add(next);

            _shopFooter.Children.Add(pager);
        }

        /// <summary>头部"$ 余额  共 N 项"(余额变化后单独更新, 不重建列表)</summary>
        private void UpdateShopHeader()
        {
            if (_shopHeadText == null)
                return;
            var core = MainDisplay.Core;
            var money = core?.Save?.Money ?? 0;
            var filtered = _shopQuery.Trim().Length > 0 || _shopFilter >= 0 || _shopOnlyAffordable;
            var pages = Math.Max(1, (_shopShownCount + ShopPageSize - 1) / ShopPageSize);
            var pageInfo = pages > 1 ? $" · 第 {_shopPage + 1}/{pages} 页" : "";
            _shopHeadText.Text = $"$ {money:0.#}    共 {_shopShownCount} 项" + pageInfo +
                                 (filtered ? $" (全部 {core?.Foods.Count ?? 0} 项)" : "");
        }

        /// <summary>性价比: 每块钱换来的饱腹+口渴, 排序用</summary>
        private static double ValueScore(Food f)
        {
            var effect = f.StrengthFood + f.StrengthDrink + f.Feeling * 0.5 + f.Strength;
            if (f.Price <= 0)
                return double.MaxValue;
            return effect / f.Price;
        }

        private Border BuildShopRow(Food food)
        {
            var core = MainDisplay.Core!;
            var money = core.Save?.Money ?? 0;
            var affordable = food.Price <= money;

            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(4, 4),
                Margin = new Thickness(0, 0, 0, 2),
                BorderBrush = Brush("#33FFFFFF"),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            // ---- 左: 名称 + 已有 + 效果 ----
            var left = new StackPanel();
            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            nameLine.Children.Add(new TextBlock
            {
                Text = food.Name,
                Foreground = affordable ? Brushes.White : Brush("#99FFFFFF"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var ownedText = new TextBlock
            {
                Foreground = TextAccent,
                FontSize = 11,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            nameLine.Children.Add(ownedText);
            left.Children.Add(nameLine);

            left.Children.Add(new TextBlock
            {
                Text = $"{food.TypeName} · {food.EffectText}",
                Foreground = Brush("#8A8A8A"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0),
            });
            grid.Children.Add(left);

            // ---- 中: 单价 ----
            var price = new TextBlock
            {
                Text = $"${food.Price:0.#}",
                Foreground = Brushes.Gold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            Grid.SetColumn(price, 1);
            grid.Children.Add(price);

            // ---- 右: 批量购买 ----
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var count in new[] { 1, 5, 10 })
            {
                var c = count;
                var btn = new Button
                {
                    Content = "×" + c,
                    FontSize = 11,
                    Padding = new Thickness(8, 2),
                    Margin = new Thickness(2, 0, 0, 0),
                    Background = Brush("#AA2E7D32"),
                    Foreground = Brushes.White,
                };
                btn.Click += (s, e) =>
                {
                    e.Handled = true;
                    BuyFood(food, c);
                };
                btns.Children.Add(btn);
            }
            Grid.SetColumn(btns, 2);
            grid.Children.Add(btns);

            row.Child = grid;

            // 整行点击 = 买 1 个 (保留老习惯); 点在按钮上时让按钮自己处理
            row.PointerPressed += (s, e) =>
            {
                if (e.Source is Visual v && (v is Button || v.FindAncestorOfType<Button>() != null))
                    return;
                e.Handled = true;
                BuyFood(food, 1);
            };

            void UpdateRow()
            {
                var nowMoney = core.Save?.Money ?? 0;
                var owned = core.Inventory?.Count(food.Name) ?? 0;
                ownedText.Text = owned > 0 ? $"[已有 {owned}]" : "";
                var can = food.Price <= nowMoney;
                price.Foreground = can ? Brushes.Gold : Brush("#88FF8A80");
                row.Opacity = can ? 1.0 : 0.55;
            }

            UpdateRow();
            _shopRowRefresh.Add(UpdateRow);
            return row;
        }

        /// <summary>
        /// 购买 N 份。钱只够一部分时按最大可买件数买, 并说明实际买到几件
        /// </summary>
        private void BuyFood(Food food, int count = 1)
        {
            var core = MainDisplay.Core;
            if (core?.Save == null || core.Inventory == null)
                return;

            count = Math.Max(1, count);

            if (food.Price <= 0)
            {
                core.Inventory.Add(food.Name, count);
                core.Inventory.Save();
                Stats.CountBuy();
                MainDisplay.ShowBubble($"白拿了 {count} 份「{food.Name}」");
                AfterBuy();
                return;
            }

            var affordable = (int)Math.Floor(core.Save.Money / food.Price);
            if (affordable <= 0)
            {
                MainDisplay.ShowBubble($"钱不够啦, 还差 {food.Price - core.Save.Money:0.#}");
                return;
            }

            var actual = Math.Min(count, affordable);
            var cost = food.Price * actual;
            core.Save.Money -= cost;
            core.Inventory.Add(food.Name, actual);
            core.Inventory.Save();
            Stats.CountBuy();

            if (actual < count)
                MainDisplay.ShowBubble($"钱只够买 {actual} 份「{food.Name}」\n花了 ${cost:0.#}, 余额 ${core.Save.Money:0.#}");
            else
                MainDisplay.ShowBubble($"买到了 {actual} 份「{food.Name}」\n花了 ${cost:0.#}, 余额 ${core.Save.Money:0.#}");

            Console.WriteLine($"[VPet] 购买: {food.Name} ×{actual} 花费 {cost:0.#} 余额 {core.Save.Money:0.#}");
            AfterBuy();
        }

        /// <summary>买完: 只刷新行状态与角标, 不重建列表(保滚动位置)</summary>
        private void AfterBuy()
        {
            UpdateShopHeader();
            foreach (var act in _shopRowRefresh)
            {
                try { act(); } catch { }
            }
            BuildShopFilterRow();
            CheckAchievementsAndNotify();
        }
    }
}
