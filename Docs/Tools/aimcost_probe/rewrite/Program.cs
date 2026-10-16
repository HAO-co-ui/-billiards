using System;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace rewrite
{
    /// <summary>
    /// 把 `Billiards.Runtime.dll` 里对 `UnityEngine.Object::op_Equality / op_Inequality` 的调用
    /// 改写成不触发 Unity ECall（`IsNativeObjectAlive`）的等价比较 —— 这样该程序集就能在 **Unity 之外**
    /// 被独立探针加载运行（仅用于量内核单步成本与预测步数，不改变任何产品代码/资产）。
    ///
    /// 只处理 **null 判断**这一类（本核里 UnityEngine.Object 的 `==` 只用于 `table == null` / `source == null`）。
    /// 语义等价性：本探针里没有"已销毁对象"，`ReferenceEquals` 与 Unity 的 `==` 结果一致。
    /// 输出：aimcost_probe/patched/Billiards.Runtime.dll
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string src = args[0];
            string dst = args[1];
            Directory.CreateDirectory(Path.GetDirectoryName(dst));

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(src));
            resolver.AddSearchDirectory("E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Data/Managed/UnityEngine");

            var readerParams = new ReaderParameters { AssemblyResolver = resolver };
            using AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(src, readerParams);

            var refEquals = asm.MainModule.ImportReference(
                typeof(object).GetMethod("ReferenceEquals", new[] { typeof(object), typeof(object) }));

            int eqPatched = 0;
            int neqPatched = 0;

            foreach (TypeDefinition type in asm.MainModule.Types)
            {
                PatchType(type, refEquals, ref eqPatched, ref neqPatched);
            }

            asm.Write(dst);
            Console.WriteLine($"op_Equality -> ReferenceEquals : {eqPatched} 处");
            Console.WriteLine($"op_Inequality -> !ReferenceEquals: {neqPatched} 处");
            Console.WriteLine("written " + dst);
            return 0;
        }

        private static void PatchType(TypeDefinition type, MethodReference refEquals, ref int eq, ref int neq)
        {
            foreach (TypeDefinition nested in type.NestedTypes)
            {
                PatchType(nested, refEquals, ref eq, ref neq);
            }

            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.HasBody)
                {
                    continue;
                }

                var il = method.Body.GetILProcessor();
                var snapshot = new System.Collections.Generic.List<Instruction>(method.Body.Instructions);
                foreach (Instruction instr in snapshot)
                {
                    if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                    {
                        continue;
                    }

                    if (!(instr.Operand is MethodReference mr) || mr.DeclaringType.FullName != "UnityEngine.Object")
                    {
                        continue;
                    }

                    if (mr.Name == "op_Equality")
                    {
                        il.Replace(instr, il.Create(OpCodes.Call, refEquals));
                        eq++;
                    }
                    else if (mr.Name == "op_Inequality")
                    {
                        Instruction call = il.Create(OpCodes.Call, refEquals);
                        il.Replace(instr, call);
                        il.InsertAfter(call, il.Create(OpCodes.Ldc_I4_0));
                        il.InsertAfter(call.Next, il.Create(OpCodes.Ceq));
                        neq++;
                    }
                }
            }
        }
    }
}
