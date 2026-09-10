using Naraka.ConfigCompiler;

// NARAKA 配置编译器。
//
//   Naraka.ConfigCompiler [--repo <仓库根目录>] [--check]
//
// 默认从可执行文件位置向上寻找仓库根目录（含 global.json 的目录）。
// --check 只比对不写盘：生成物与源表不一致时返回非 0 退出码，供 CI 门禁使用。
//
// 该工具只读取 Config/Source 下的 CSV，不读取 .env、连接串或任何环境变量值，
// 因此可以在任何环境安全运行，输出中也不会出现敏感信息。
return ConfigCompilerEntryPoint.Run(args, Console.Out, Console.Error);
