using System.Collections.Generic;
using Billiards.Core;
using Billiards.Physics;
using UnityEngine;

namespace Billiards.UI
{
    /// <summary>HUD 三块的视口矩形（`x`/`y` = 左下原点、`width`/`height` 为比例，全部 ∈ [0,1]）。</summary>
    public struct PracticeHudRects
    {
        /// <summary>计数组（剩余球数 / 杆数 / 已进球）。</summary>
        public Rect counters;

        /// <summary>状态条（`Aim` / `Moving`）。</summary>
        public Rect status;

        /// <summary>力度块（`POWER` + 百分比 + 横条 + `Shoot`）。</summary>
        public Rect power;
    }

    /// <summary>
    /// **最小 HUD 的布局单点**（`Docs/35-A_Day13_最小HUD_spec.md` §4）。
    ///
    /// ── 为什么全部落在「顶带」（几何硬约束，不是审美选择）──────────────────────
    /// 本工程的相机构图（球桌几乎充满视口）实测给出：**顶带空余 0.235298**（16:9，六档中最紧）、
    /// **底带仅 0.093984**、**左右各仅 0.049899**。参考实现的"右侧竖栏 + 底部横条"在本工程**摆不下**；
    /// 底带还被球桌近侧库边/木框占据 ⇒ 三块一律落在顶带 `y ∈ [0.780, 0.982]`。
    /// 定量依据与六档投影表见 spec §3.3。
    ///
    /// ── 盲带（如实）────────────────────────────────────────────────────────
    /// 本类只保证**几何不变量**（不越界 / 不压球桌 / 互不重叠 / 字号下限）。
    /// **文字在块内的实际像素溢出**、颜色观感、间距舒适度均**不在**判据覆盖内（`-nographics` 无渲染）。
    /// </summary>
    public static class PracticeHudLayout
    {
        // ── 画布基准（`CanvasScaler.ScaleWithScreenSize` + `match = 0.5`）──────
        public const int ReferenceWidth = 1920;
        public const int ReferenceHeight = 1080;

        /// <summary>`matchWidthOrHeight`（0 = 只按宽、1 = 只按高）。</summary>
        public const float MatchWidthOrHeight = 0.5f;

        // ── 三块分区（视口比例，规格 §4.1 逐字）────────────────────────────
        // y 上界 0.982 < 1（上边距 0.018）；y 下界 0.780 > 球桌最高投影 0.764702（余量 0.015298）。
        public static readonly Rect CountersZone = new Rect(0.020f, 0.780f, 0.280f, 0.202f);
        public static readonly Rect StatusZone = new Rect(0.360f, 0.780f, 0.280f, 0.202f);
        /// <summary>
        /// 力度块。⚠ **不能放到屏幕右侧竖排**：球桌主区已占到 `x ≤ 0.9501`、`y 0.094–0.765`
        /// （判据 `T95 (b)` 明确禁止 HUD 压球桌）⇒ 右侧只剩 0.05 宽、放不下 150 px 的 `Shoot` 按钮。
        /// 参考稿那种"右侧一列"在本工程会盖住球桌边缘，故保持**顶部右块**。
        /// </summary>
        public static readonly Rect PowerZone = new Rect(0.745f, 0.792f, 0.235f, 0.190f);

        // ── 可读性下限（规格 §4.2）──────────────────────────────────────────
        /// <summary>小标签 / 状态 / 按钮文案的设计字号（1080 域像素）。</summary>
        public const float LabelFontSize = 14f;

        /// <summary>数值大字的设计字号（1080 域像素）。</summary>
        public const float ValueFontSize = 26f;

        /// <summary>文本在 **1280×720** 下的实际像素高下限。</summary>
        public const float MinReadablePixels = 9f;

        /// <summary>
        /// `Shoot` 按钮在**1080 域**下的**可读性下限宽度**。
        ///
        /// ⚠ **它不是设计稿的按钮宽** —— 两者是不同的量，本轮曾误合并成一个数，导致判据 `T95 (e)` 变红：
        /// · **本常量（150）** = "**在 1280×720 上渲染出来仍 ≥ 90 px**"反推的宽度：
        ///   `90 ÷ CanvasScale(1280,720)` = `90 ÷ 0.6667` ≈ 135，取 **150** 留余量。
        ///   它是**可读性预算的下限**（`T95 (e)` 就按它算 720p 渲染宽）。
        /// · **设计稿的按钮宽 = `ClubUiTokens.ButtonWidth`（112）** = 原型 `.shoot-button { width: 112px }`
        ///   在 **1920×1080** 设计分辨率下的**参考值**。112 在 720p 上只有 74.7 px，
        ///   **低于** 90 px 的下限 —— 但这不是问题，因为**本工程画得比它宽**：
        ///   竖排力度块里按钮取**整栏宽**（= `PowerZone.width × 1920` ≈ **537.6** 设计像素，
        ///   与设计稿 `@media (min-width: 900px) .shoot-button { width: 100% }` 同形）。
        ///
        /// ⇒ **别把这两个数合并**：一个是"最小可读宽度"，一个是"原型的参考宽度"，
        /// 合并后要么误报红（如本轮），要么把设计参考值当成硬约束。
        /// 视图侧另有一条构建期自检：若本常量 > 力度块宽，会在构建时告警（见 `PracticeHudView.BuildShotbar`）。
        /// </summary>
        public const float ShootButtonWidth = 150f;

        /// <summary>`Shoot` 按钮在 1080 域下的**设计参考高度**（= `ClubUiTokens.ButtonHeight` 的转发）。</summary>
        public const float ShootButtonHeight = ClubUiTokens.ButtonHeight;

        // ── 球桌口径（唯一来源 = `PoolConstants` / `PoolGeometry`，此处不另立数值）──
        /// <summary>台面矩形的四个物理角（`PoolGeometry` 是唯一坐标转换点，铁律 16）。</summary>
        public static IReadOnlyList<Vector2> ClothCorners { get; } = new[]
        {
            new Vector2(-PoolConstants.ClothInnerLength * 0.5f, -PoolConstants.ClothInnerWidth * 0.5f),
            new Vector2(PoolConstants.ClothInnerLength * 0.5f, -PoolConstants.ClothInnerWidth * 0.5f),
            new Vector2(-PoolConstants.ClothInnerLength * 0.5f, PoolConstants.ClothInnerWidth * 0.5f),
            new Vector2(PoolConstants.ClothInnerLength * 0.5f, PoolConstants.ClothInnerWidth * 0.5f),
        };

        /// <summary>
        /// 三块分区。**注**：规格 §5.3 曾写作 `Resolve(float aspect)`，实现改为**无参** ——
        /// 三块是**视口比例常量**，宽高比不进入求解 ⇒ 保留该参数只会得到一个"传了不用"的空参数（死参数）。
        /// 该偏离已登记（规格只钉死分区的**数值**，未钉死"必须依赖 aspect"）。
        /// </summary>
        public static PracticeHudRects Resolve()
        {
            return new PracticeHudRects
            {
                counters = CountersZone,
                status = StatusZone,
                power = PowerZone,
            };
        }

        /// <summary>转发 <see cref="CameraViewportFitter.ResolveVerticalFovDeg"/>（同一不变量单点实现，铁律 16）。</summary>
        public static float ResolveVerticalFovDeg(float aspect, float baseAspect, float baseVerticalFovDeg, float maxVerticalFovDeg)
        {
            return CameraViewportFitter.ResolveVerticalFovDeg(aspect, baseAspect, baseVerticalFovDeg, maxVerticalFovDeg);
        }

        /// <summary>
        /// **针孔投影**：世界点 → 视口（`(0,0)` = 左下）。
        /// 相机无 roll（`up = +Y`）⇒ `right = normalize(cross(up, forward))`、`upCam = cross(forward, right)`。
        /// **刻意不用 `Camera.WorldToViewportPoint`** —— 判据要能在**没有相机对象**时算（纯函数、可复现）。
        /// </summary>
        public static Vector2 ProjectToViewport(Vector3 worldPoint, Vector3 cameraPosition, Vector3 cameraForward,
                                                float verticalFovDeg, float aspect)
        {
            Vector3 forward = cameraForward.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right);

            Vector3 v = worldPoint - cameraPosition;
            float z = Vector3.Dot(v, forward);
            float x = Vector3.Dot(v, right);
            float y = Vector3.Dot(v, up);

            if (!(Mathf.Abs(z) > 1e-6f))
            {
                return new Vector2(float.NaN, float.NaN);   // 退化：点在相机平面上（本工程用不到）
            }

            float halfH = Mathf.Tan(verticalFovDeg * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * aspect;
            return new Vector2(0.5f + 0.5f * (x / z / halfW), 0.5f + 0.5f * (y / z / halfH));
        }

        /// <summary>
        /// **球桌主区**在视口的投影包围盒 = 台面（内库）矩形四角的投影包围盒。
        /// 四角经 `playfieldRoot.TransformPoint(PoolGeometry.PhysicsToUnity(corner, 0))` 得世界点 ⇒
        /// 显示缩放（`PlayfieldVisualScale`）**由变换本身提供**，本类不重复乘。
        /// 台面在 `y = 0`（台呢面）。
        /// </summary>
        public static Rect TableViewportRect(Transform playfieldRoot, Vector3 cameraPosition, Vector3 cameraForward,
                                             float verticalFovDeg, float aspect)
        {
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;

            foreach (Vector2 corner in ClothCorners)
            {
                Vector3 world = playfieldRoot == null
                    ? PoolGeometry.PhysicsToUnity(corner, 0f)
                    : playfieldRoot.TransformPoint(PoolGeometry.PhysicsToUnity(corner, 0f));

                Vector2 viewport = ProjectToViewport(world, cameraPosition, cameraForward, verticalFovDeg, aspect);
                minX = Mathf.Min(minX, viewport.x);
                maxX = Mathf.Max(maxX, viewport.x);
                minY = Mathf.Min(minY, viewport.y);
                maxY = Mathf.Max(maxY, viewport.y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        /// <summary>视口矩形 → 像素矩形（`(0,0)` = 左下）。</summary>
        public static Rect ToScreenPixels(Rect viewportRect, int screenWidth, int screenHeight)
        {
            return new Rect(
                viewportRect.xMin * screenWidth,
                viewportRect.yMin * screenHeight,
                viewportRect.width * screenWidth,
                viewportRect.height * screenHeight);
        }

        /// <summary>
        /// **画布缩放系数**：照抄 `CanvasScaler` 在 `ScaleWithScreenSize` + `match=0.5` 下的对数插值
        /// （不是 `min(w,h)/1080` 的近似 —— 那在非 16:9 档会算错）。
        /// </summary>
        public static float CanvasScale(int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return 1f;
            }

            float logWidth = Mathf.Log((float)screenWidth / ReferenceWidth, 2f);
            float logHeight = Mathf.Log((float)screenHeight / ReferenceHeight, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, MatchWidthOrHeight));
        }

        /// <summary>
        /// 某设计字号在给定分辨率下的**实际像素高**（"两档可读"的可计算口径）。
        /// ⚠ 乘 <see cref="FontScale"/> —— 与 `PracticeHudView.CreateText` **同一个常量、同一处口径**
        /// （两处不一致 => 这条判据预测的就不是屏幕上真实的东西）。**别在这里乘第二次**（`T148(d)` 守）。
        /// </summary>
        public static float RenderedFontPixels(float designFontSize, int screenWidth, int screenHeight)
        {
            return designFontSize * CanvasScale(screenWidth, screenHeight) * FontScale;
        }

        /// <summary>
        /// **全局字号倍率 —— 唯一实现点**（用户实机反馈"字体太小，看不清" ⇒ 整体放大）。
        ///
        /// ── 为什么必须是一个常量、而不是逐个字号手调 ──────────────────────────────
        /// 设计稿给的是**设计像素**（8 / 9 / 11 / 12 / 23 / 38 …），它们散在 `ClubUiTokens` 的字号阶梯里，
        /// 且同时参与了各块的高度预算。逐个调会立刻散成"这块大一点、那块忘了调"的半新半旧界面
        /// （与 35-A 重做前"令牌散落在两处视图"是同一类病）。⇒ **一个倍率 × 所有设计字号**。
        ///
        /// ── ⚠ 乘在哪：**不是** `RenderedFontPixels`，而是 `PracticeHudView.CreateText` ─────
        /// 实测事实：`RenderedFontPixels` **全库只有一个调用者**，是**判据**
        /// （`PracticeHudTests.T95 (e)` 的"1280×720 可读下限"）—— 它算的是"**预测**出来的物理像素高"，
        /// **不驱动任何渲染**。真正写字号的地方**也只有一个**：`PracticeHudView.CreateText` 的
        /// `text.fontSize = size`（HUD / 首页 / 赛前屏 / 结算卡 / 选袋条**全部**走它）。
        /// ⇒ 倍率**必须**乘在 `CreateText`（那才叫"整体放大"）；`RenderedFontPixels` 乘**同一个** `FontScale`，
        ///   否则它预测的像素高与实渲染不一致 ⇒ 可读性判据会变成**假陈述**。
        /// ⇒ 两处引用**同一个常量** ⇒ 倍率仍然只有一个真源（判据 `T148(d)` 守"只准乘两次"）。
        ///
        /// ── 取值 **1.6** 的由来（**两轮用户反馈**，不是审美）────────────────────
        /// · **参考分辨率 = 1920×1080**：`CanvasScale(1920,1080) = 1` ⇒ **设计像素 = 物理像素**。
        ///   所以下面的数字就是"1080p 屏幕上真实多高"。
        /// · **三轮反馈：1.28 不够 → 1.6 不够 → 1.9 才够**（用户原话"小，但很清楚" ⇒ 不是糊/不是挤/不是视距）。
        ///   1.28 时正文 15.4 px、1.6 时 19.2 px，用户**两次**说看不清 ⇒ 现值 **1.9**：
        ///   正文 `12 → 22.8 px`、标题 `38 → 72.2 px`、标签 `9 → 17.1 px`、结算统计 `27 → 51.3 px`。
        ///   ⇒ 判据 `T148(g)` 把"正文 ≥ 22 px / 标签 ≥ 16 px"钉成**可读下限**
        ///   （旧阈值 18/13 下，回调到 1.5 时正文正好 18 px 仍会绿 —— 那正是要防的滑坡）。
        /// · 排期小档 1280×720（`CanvasScale = 0.667`）：正文 `12 → 15.2 px`、标签 `9 → 11.4 px`。
        /// · **4K 不会被抵消**：`CanvasScale(3840,2160) = 2`（见 `CanvasScale` 的对数插值）⇒ 4K 下正文实测 38.4 px。
        ///   真正影响观感的是**面板物理尺寸/DPI**，不是分辨率；若用户 4K 上仍嫌小，那是另一条口径（按 DPI），不在这里。
        /// </summary>
        public const float FontScale = 1.9f;

        /// <summary>`inner ⊆ outer`（四条边逐条判，容差 0）。</summary>
        public static bool ContainsFully(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin && inner.xMax <= outer.xMax
                && inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;
        }


    }
}
