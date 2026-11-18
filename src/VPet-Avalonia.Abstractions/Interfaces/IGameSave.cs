namespace VPetAvalonia.Abstractions.Interfaces
{
    /// <summary>
    /// 游戏存档抽象接口
    /// </summary>
    public interface IGameSave
    {
        /// <summary>
        /// 保存游戏数据
        /// </summary>
        void Save();
        
        /// <summary>
        /// 加载游戏数据
        /// </summary>
        void Load();
        
        /// <summary>
        /// 获取或设置存档数据
        /// </summary>
        /// <param name="key">数据键</param>
        /// <returns>数据值</returns>
        string this[string key] { get; set; }
    }

    /// <summary>
    /// 宠物状态模式
    /// </summary>
    public enum ModeType
    {
        /// <summary>
        /// 高兴
        /// </summary>
        Happy,
        /// <summary>
        /// 普通
        /// </summary>
        Nomal,
        /// <summary>
        /// 状态不佳
        /// </summary>
        PoorCondition,
        /// <summary>
        /// 生病(躺床)
        /// </summary>
        Ill
    }
}
