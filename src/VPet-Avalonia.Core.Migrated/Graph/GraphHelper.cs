using VPetAvalonia.Abstractions;
using System;
using System.Collections.Generic;
using VPetAvalonia.Core;

namespace VPetAvalonia.Core
{
    public static class GraphHelper
    {
        private static string[][]? graphtypevalue;
        
        /// <summary>
        /// 动画类型默认前文本
        /// </summary>
        public static string[][] GraphTypeValue
        {
            get
            {
                if (graphtypevalue == null)
                {
                    List<string[]> gtv = new List<string[]>();
                    foreach (string v in Enum.GetNames(typeof(GraphInfo.GraphType)))
                    {
                        gtv.Add(v.ToLowerInvariant().Split('_'));
                    }
                    graphtypevalue = gtv.ToArray();
                }
                return graphtypevalue;
            }
        }
    }
}
