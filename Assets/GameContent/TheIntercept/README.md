# 《密电疑云》中文化与接入说明

- 固定来源：`inkle/the-intercept` 提交 `2a816b56e61ce4bf02bec1c638074645bdd871e3`。
- `Translation/TheIntercept.en.ink.txt` 是未经修改的英文基线，SHA-256 为 `E4DA21BBA77600BE154F93D34F7BCD1E61622DAA83F4213356779BC5838BDFD6`。
- `Translation/TheIntercept.zh-CN.ink.txt` 是已完成全文汉化和语义画面描述的审核基线，SHA-256 为 `771A90C27047F0EEF0B52C9D8BA1EE949B83F0F3A568982219267BD4D7604A06`。
- `TheIntercept.zh-CN.ink` 是 Unity 正式导入的可玩剧情，内容与上述审核基线完全一致。
- 正式中文名为《密电疑云》，稳定 `gameId` 为 `the-intercept`，独立存档名为 `GameProgress_the-intercept`。`The Intercept` 仅在来源与许可信息中保留。
- 翻译必须保留所有变量、常量、knot、stitch、选择层级、命名标签、赋值、divert、tunnel 和条件表达式。
- 每个稳定场景使用 `# title:` 与 `# visual:` 提供简短标题和真实画面描述；选择文字必须能脱离视觉位置独立理解。
- 全文校验运行 `Tools/Validate-TheInterceptTranslation.ps1 -RequireNoEnglish`，四结局可达性运行 `Tools/Validate-TheInterceptPaths.ps1`。当前结构、固定编译器、严格英文残留、四结局路径和 Unity 正式接入回归均已通过。

版权与许可见同目录 `LICENSE-TheIntercept.txt`。本工作区不包含原仓库的音频、字体、图标、背景或 PSD。
