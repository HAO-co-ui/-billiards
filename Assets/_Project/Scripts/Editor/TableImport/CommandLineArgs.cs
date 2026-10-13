using System;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 读取 `Unity.exe -executeMethod ... -outputDir &lt;path&gt;` 形式的自定义命令行参数。
    /// 纯 IO 管道（不含任何几何/判定口径），置于此处以避免两个校验器各写一份。
    /// </summary>
    public static class CommandLineArgs
    {
        public static string Get(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
