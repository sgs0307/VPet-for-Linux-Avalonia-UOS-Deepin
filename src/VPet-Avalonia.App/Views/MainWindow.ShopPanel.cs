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
        private TextBlock? _shopHeadText;   // 头部副标题: "$ 123 · 共 N 项 · 第 1/6 页"
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
                // 列表区用面板的 body, 工具栏挂在标题下面(不随列表滚走);
                // 余额/条数放进标题下的副标题, 比在列表顶上塞一行金色小字更稳(不会滚走)
                _shopPanel = CreatePanel("商店", 440, 640,
                    out _shopBody, out _shopTools, out _shopFooter, out _shopHeadText, 92, 46);
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

            _shopSearch = new TextBox();
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
            _shopTools.Children.Add(UiSearch(_shopSearch, "搜索名字 / 类型, 或输入价格上限(如 50)"));

            _shopFilterRow = new StackPanel { Orientation = Orientation.Horizontal };
            _shopTools.Children.Add(_shopFilterRow);

            _shopSortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
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

            void AddPill(string label, Food.FoodType? type, int badge)
            {
                var active = type == null ? _shopFilter == (Food.FoodType)(-1) : _shopFilter == type;
                var caption = badge > 0 ? $"{label} {badge}" : label;
                var pill = UiPill(caption, active, () =>
                {
                    _shopFilter = type ?? (Food.FoodType)(-1);
                    _shopPage = 0;
                    BuildShopFilterRow();
                    RefreshShopList();
                    UpdateClickThrough();
                }, small: true);
                _shopFilterRow!.Children.Add(pill);
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
                Foreground = UiFaint,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });

            for (int i = 0; i < ShopSortNames.Length; i++)
            {
                var idx = i;
                var pill = UiPill(ShopSortNames[i], _shopSort == i, () =>
                {
                    _shopSort = idx;
                    _shopPage = 0;
                    BuildShopSortRow();
                    RefreshShopList();
                    UpdateClickThrough();
                }, small: true);
                _shopSortRow.Children.Add(pill);
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
                Spacing = 6,
            };

            var prev = UiAction("上一页", BtnGray, BtnGrayHover, () =>
            {
                if (_shopPage > 0) _shopPage--;
                RefreshShopList();
                UpdateClickThrough();
            }, page > 0, noMargin: true);
            pager.Children.Add(prev);

            pager.Children.Add(new TextBlock
            {
                Text = $"{page + 1} / {pages}",
                Foreground = TextDim,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
            });

            var next = UiAction("下一页", BtnGray, BtnGrayHover, () =>
            {
                if (_shopPage < pages - 1) _shopPage++;
                RefreshShopList();
                UpdateClickThrough();
            }, page < pages - 1, noMargin: true);
            pager.Children.Add(next);

            _shopFooter.Children.Add(pager);
        }

        /// <summary>头部副标题"余额 · 共 N 项 · 第 x/y 页"(余额变化后单独更新, 不重建列表)</summary>
        private void UpdateShopHeader()
        {
            if (_shopHeadText == null)
                return;
            var core = MainDisplay.Core;
            var money = core?.Save?.Money ?? 0;
            var filtered = _shopQuery.Trim().Length > 0 || _shopFilter >= 0 || _shopOnlyAffordable;
            var pages = Math.Max(1, (_shopShownCount + ShopPageSize - 1) / ShopPageSize);
            var pageInfo = pages > 1 ? $" · 第 {_shopPage + 1}/{pages} 页" : "";
            _shopHeadText.Text = $"余额 $ {money:0.#} · 共 {_shopShownCount} 项{pageInfo}" +
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

        /// <summary>分类配色: 正餐橙 / 饮料蓝 / 零食粉 / 药品绿 / 礼品紫 (和角色面板属性条同一套色)</summary>
        private static IBrush FoodTypeBrush(Food.FoodType t) => t switch
        {
            Food.FoodType.Meal => UiOrange,
            Food.FoodType.Drink => UiCyan,
            Food.FoodType.Snack => Brush("#F06292"),
            Food.FoodType.Drug => UiGreen,
            Food.FoodType.Gift => UiPurple,
            _ => UiMuted,
        };

        private Border BuildShopRow(Food food)
        {
            var core = MainDisplay.Core!;
            var money = core.Save?.Money ?? 0;
            var affordable = food.Price <= money;

            // ---- 左: 名称 + 已有 + 类型/效果 ----
            var nameText = new TextBlock
            {
                Text = food.Name,
                Foreground = affordable ? Brushes.White : Brush("#99FFFFFF"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var ownedText = new TextBlock
            {
                Foreground = UiCyan,
                FontSize = 10,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            nameLine.Children.Add(nameText);
            nameLine.Children.Add(ownedText);

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
                Text = " · ",
                Foreground = UiFaint,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            metaLine.Children.Add(new TextBlock
            {
                Text = food.EffectText,
                Foreground = UiMuted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            info.Children.Add(nameLine);
            info.Children.Add(metaLine);

            // ---- 右: 单价 + 批量按钮(上下两行, 比横排三个按钮省宽度) ----
            var price = new TextBlock
            {
                Text = $"${food.Price:0.#}",
                Foreground = affordable ? UiGold : Brush("#88FF8A80"),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 3, 0, 0),
            };
            var first = true;
            foreach (var count in new[] { 1, 5, 10 })
            {
                var c = count;
                var pill = UiAction("×" + c,
                    c == 1 ? BtnGreen : BtnGreenDeep,
                    c == 1 ? BtnGreenHover : BtnGreenDeepHover,
                    () => BuyFood(food, c), noMargin: first);
                first = false;
                btns.Children.Add(pill);
            }

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(price);
            right.Children.Add(btns);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.Children.Add(info);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            var card = UiCard(grid, 10, 6, 4);
            UiHover(card);
            card.Opacity = affordable ? 1.0 : 0.55;

            // 整行点击 = 买 1 个 (保留老习惯)
            card.PointerPressed += (s, e) => BuyFood(food, 1);

            void UpdateRow()
            {
                var nowMoney = core.Save?.Money ?? 0;
                var owned = core.Inventory?.Count(food.Name) ?? 0;
                ownedText.Text = owned > 0 ? $"[已有 {owned}]" : "";
                var can = food.Price <= nowMoney;
                price.Foreground = can ? UiGold : Brush("#88FF8A80");
                card.Opacity = can ? 1.0 : 0.55;
            }

            UpdateRow();
            _shopRowRefresh.Add(UpdateRow);
            return card;
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
