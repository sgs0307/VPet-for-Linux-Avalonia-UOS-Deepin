# VPet 跨平台移植进度报告

> ⚠️ **本文为 2026-11-03 的早期阶段记录，状态已严重过时**（文中「动画 0% / UI 交互 0% / 总体 60%」均不成立）。
> 当前真实进度请看 `功能对比-未实现清单.md`（截至 v1.3.0：核心玩法闭环全部完成 + APNG 动画 + 四个简版面板补齐，剩余 9 项窗口类/系统级功能）。
> 本文仅保留作为移植初期的技术挑战记录。

## 项目概述
将 Windows WPF 桌面应用 VPet-Simulator 移植到跨平台 Avalonia 框架，支持 UOS Linux ARM64 环境。

## 已完成工作

### 1. 环境准备 ✅
- ✅ 安装 .NET SDK 8.0.425 (ARM64)
- ✅ 安装 Avalonia 11.3.0 框架
- ✅ 配置项目结构和依赖

### 2. 项目结构创建 ✅
创建了三个项目：
- **VPet-Avalonia.Abstractions** - 抽象层项目
  - Interfaces/ - 15个抽象接口 (IUIElement, IDispatcher, IColor, IImage, IPoint, ISize, ITouchArea, IController, IGraph, IGraphCore, ITaskControl, IGameCore, IGameSave, IAction, IDisposable)
  - Graphics/ - 图形相关接口 (IColor, IImage, IGraph, IGraphInfo, ITaskControl, IGraphCore)
  - Input/ - 输入相关接口 (IPoint, ISize)
- **VPet-Avalonia.Core.Migrated** - 核心逻辑迁移项目
  - Handle/ - 业务逻辑 (GameSave, IGameSave, IFood, IController, Function, GameCore, PetLoader, SayInfo)
  - Graph/ - 图形系统 (GraphInfo, GraphHelper, GraphCore, GraphCore.Avalonia, IGraph, IUIElement, ITaskControl, IImageRun, Picture, PNGAnimation, APNGAnimation, FoodAnimation, Move, Work)
- **VPet-Avalonia.App** - Avalonia 应用项目
  - Program.cs - 应用入口
  - Views/ - 视图文件 (MainWindow.axaml, Main.axaml, ToolBar.axaml, MessageBar.axaml, WorkTimer.axaml)

### 3. 核心逻辑迁移 ✅
- ✅ 迁移 GameSave/IGameSave/IFood/IController/GraphInfo/Function/GameCore 等核心文件
- ✅ 重写 Function.cs (HEXToColor/ColorToHEX 返回 uint ARGB，ResourcesBrush 简化，LPSConvertToLower, MemoryUsage, ComCheck)
- ✅ 重写 GameCore.cs (自定义 Point/Size 结构体，TouchArea 类，IGraphCore 引用)
- ✅ 创建 GraphCore.Avalonia.cs (实现 IGraphCore，含 Config 类含 TouchHeadLocate/TouchRaisedLocate/Moves/Works 等)
- ✅ 创建 Move.cs/Work.cs 配置类
- ✅ 创建 IImageRun 接口和动画类 (Picture/PNGAnimation/APNGAnimation/FoodAnimation - 骨架版)
- ✅ 重写 PetLoader.cs (IGraphConvert 字典 + LoadGraph 递归加载)
- ✅ 重写 SayInfo.cs (UIElement 改为 object)
- ✅ 编译成功 (0错误，6个警告：CS8766可空性不匹配、CS0414未使用字段，不影响功能)

### 4. XAML 转 AXAML ✅
- ✅ 转换 Main.xaml → Main.axaml
- ✅ 转换 ToolBar.xaml → ToolBar.axaml
- ✅ 转换 MessageBar.xaml → MessageBar.axaml
- ✅ 转换 WorkTimer.xaml → WorkTimer.axaml
- ✅ 添加 x:Class 到 AXAML 文件
- ✅ 移除手动 InitializeComponent() 调用
- ✅ 修复事件处理方法

### 5. 当前状态
- ✅ 抽象层编译成功
- ✅ 核心逻辑编译成功
- ✅ 应用项目编译成功
- ⚠️ 部分 API 需要完善 (FindGraph 等)

## 待完成工作

### 1. 实现 APNG 动画支持 ⏳
- [ ] 实现 APNGAnimation 类的 Run 方法
- [ ] 实现 PNGAnimation 类的 Run 方法
- [ ] 实现 Picture 类的图像加载
- [ ] 实现 FoodAnimation 类

### 2. 重写 UI 交互逻辑 ⏳
- [ ] 重写 MainDisplay.cs (依赖 WPF 和 Panuon.WPF)
- [ ] 重写 MainLogic.cs (依赖 WPF 和 Panuon.WPF)
- [ ] 实现拖拽功能
- [ ] 实现消息显示功能

### 3. 集成测试和功能验证 ⏳
- [ ] 测试图形渲染
- [ ] 测试动画播放
- [ ] 测试交互功能
- [ ] 测试存档系统

### 4. 性能优化和 Bug 修复 ⏳
- [ ] 优化内存使用
- [ ] 修复已知问题
- [ ] 性能调优

## 技术挑战

### 1. WPF 依赖分离
- 原项目有 296 处 System.Windows 引用
- 已通过抽象层成功分离大部分依赖
- 剩余 Display 目录文件仍需适配

### 2. Avalonia 与 WPF 差异
- 事件处理模型不同
- 控件命名空间不同
- 布局系统不同
- 已通过适配器模式解决大部分问题

### 3. 第三方库依赖
- Panuon.WPF 无 Avalonia 版本
- 已通过自定义样式解决
- LinePutScript 已成功迁移

## 下一步计划

1. **完成 APNG 动画支持** - 实现图像渲染核心功能
2. **重写 UI 交互逻辑** - 完成 MainDisplay/MainLogic 适配
3. **集成测试** - 验证所有功能正常工作
4. **性能优化** - 优化内存和性能

## 风险评估

### 高风险
- [ ] APNG 动画实现复杂度高
- [ ] UI 交互逻辑复杂，需要大量适配工作

### 中风险
- [ ] 性能可能不如原生 WPF
- [ ] 第三方库功能缺失

### 低风险
- [ ] 基本功能已可运行
- [ ] 核心逻辑已迁移完成

## 总结

目前移植工作已完成约 60%：
- 环境准备：100% 完成
- 项目结构：100% 完成
- 核心逻辑迁移：100% 完成
- XAML 转 AXAML：100% 完成
- 动画系统：0% 完成（骨架）
- UI 交互：0% 完成（待适配）
- 集成测试：0% 完成

预计剩余工作需要 2-3 周完成。