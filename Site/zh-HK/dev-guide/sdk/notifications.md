# 通知卡片

`Lertaro.PluginSdk.Services.PluginNotificationService` 讓外掛模組能透過宿主自身的視窗從背景取得使用者注意——不必另開程序、不走作業系統的通知通道，也不與宿主 UI 組件耦合。任何內容停留在畫面上的時間與位置都由宿主決定，而不是外掛模組。

## 1. 呼叫入口

| 簽章 | 返回 | 使用時機 |
| :--- | :--- | :--- |
| `INotificationHandle Show(NotificationRequest request)` | 立即取得控制代碼 | 你之後要取代或取消這則通知 |
| `Task<NotificationResult> ShowAsync(NotificationRequest request)` | 結束狀態 | 你只在乎它怎麼收場 |
| `bool Show(string title, string text, Action? onClick = null)` | 是否有宿主接受 | 既有的兩參數形式；返回 `false` 代表沒有任何宿主接手，因此可以準備退路 |

每一個入口都可以安全地從外掛模組自己的背景執行緒呼叫，而且**絕不擲出例外**——竄出的例外會把呼叫端的迴圈一起拖垮。當完全沒有指派宿主委派時（也就是外掛模組運行在啟動器之外），這個呼叫就是一次安靜的無作用操作，答覆是一枚其工作已經以 `Unavailable` 結束的控制代碼；只有在宿主自己的委派擲出例外時，該請求才會以 `Warn` 等級記錄。

`INotificationHandle` 恰好只有兩個成員：`Task<NotificationResult> Completion` 與 `void Dismiss()`。`Completion` **一定**會完成，包括沒有任何內容曾經上螢幕的情況，因此 `await` 它不會困住執行緒。`Dismiss()` 會像使用者自行關閉那樣關掉已顯示的卡片，卡片一旦消失它就是無作用操作，而且絕不會翻轉已經交付的結果。

需要自己擁有視窗的外掛模組請改用 [`Windows.PluginWindow`](./services)；需要的是*一個答案*而非單向告知的，請改用 `PluginMessageBoxService`。

## 2. `NotificationRequest`

| 屬性 | 預設值 | 行為 |
| :--- | :--- | :--- |
| `string? Id` | `null` | 兩個 `Id` 相同的請求指的其實是同一則通知。詳見**以 Id 取代**（§4）。 |
| `string Title` | `string.Empty` | 卡片標題。`BottomNotice` 沒有標題列，會直接忽略它。 |
| `string Message` | `string.Empty` | 內文文字。`Title` 與 `Message` **兩者都為空白或只含空白字元**的請求會被以 `InvalidRequest` 拒絕——因為沒有東西可顯示。 |
| `NotificationLevel Level` | `Info` | `Info` / `Warn` / `Error`。只決定圖示與語意顏色，別無其他。 |
| `NotificationPosition Position` | `CardStack` | `CardStack` 或 `BottomNotice`。兩者各有自己的佇列與自己的規則，永遠不會互相阻擋。 |
| `double? DurationSeconds` | `null` | `null` 採用該位置自己的預設值。超出該位置允許範圍的明確數值會**被裁剪到最近的邊界**，而不會被拒絕；`NaN` 或負數會落在較短的那一端。 |
| `Action? OnClick` | `null` | 在關閉之外額外執行。詳見**點擊回呼**（§6）。 |

## 3. 兩個位置

| | `CardStack`（右下角） | `BottomNotice`（下方置中） |
| :--- | :--- | :--- |
| 形態 | 帶標題、來源名稱、✕ 與「全部標為已讀」的卡片 | 單行；無標題、無來源、無控制項 |
| 同時顯示 | 可見 5 張，其餘在佇列中等待 | 1 則——新的立即取代目前那則，沒有拆解動作需要等待 |
| 預設時長 | 8 秒 | 4 秒 |
| 裁剪範圍 | 2 – 30 秒 | 2 – 10 秒 |
| `OnClick` | 會履行 | 忽略（沒有任何可互動的元素） |
| 全螢幕應用取得畫面 | 收攏為單行提示，上限 5 秒 | 不受影響 |

批次發出通知時，還有兩個節奏數字值得留意：

- 宿主只跑**一個 100 ms 的時脈跳動**，而每個跳動**最多**提昇一張等待中的卡片。因此一批通知會自行拉開彼此的登場時間；又因為每張卡片從自己出現的那一刻起計時，拉開登場也就拉開了到期——一批通知再也不會一次就佔掉堆疊中的五個位置。
- **單一外掛模組最多有 10 個被受理的卡片請求**（畫面上的*與*等待中的合計）。第十一個會以 `QueueFull` 丟棄並記錄。這個上限只在卡片的准入路徑上強制執行：`BottomNotice` 請求絕不會被計入，因為那一行提示是會自我覆寫的單一槽位。跨外掛模組沒有全域的等待上限，因此某個外掛模組不會被另一方的批次餓死，但失控的生產者也不會受到全域的限制。

## 4. 以 Id 取代

為請求指定 `Id`，正是不斷更新的狀態訊息不致於把畫面塞滿自己複本的做法：`Id` 與某張**可見**卡片相符的新請求會就地覆寫那張卡片——仍留在堆疊中的同一個位置，並不會跳到最上面——而先前那位呼叫端的控制代碼會以 `Replaced` 完成。需要知道自己被取代的呼叫端，恰恰就是那些留著控制代碼的呼叫端。

有一個必須尊重的前提：去重只對照目前畫面上的卡片，**不**對照仍在佇列中等待的卡片。畫面已滿時連續發出五份相同 `Id` 的外掛模組，這五份全都會進入佇列。

## 5. 結果與失敗原因

`NotificationResult` 是 `Succeeded` 加上一個選填的 `NotificationFailure`。第一個抵達該項目的終止狀態即定案，之後沒有任何東西會改寫它。

| `NotificationFailure` | 發生時機 |
| :--- | :--- |
| `Unavailable` | 沒有任何宿主處理該請求——外掛模組運行在啟動器之外，或宿主委派擲出了例外。 |
| `InvalidRequest` | `Title` 與 `Message` 都是空白或只含空白字元。 |
| `QueueFull` | 該外掛模組已經有 10 個卡片請求被受理。 |
| `Replaced` | 有更新的請求帶著相同的 `Id`。`BottomNotice` 的覆寫會讓被取代的請求以這個方式完成，**即使兩者都沒帶 `Id`** 也是如此，因為那一行提示是永遠會覆寫的單一槽位。 |
| `CancelledByPluginUnload` | 外掛模組在卡片顯示中或仍在佇列時被停用或卸載。 |
| `HostShuttingDown` | 啟動器正在結束，並把通知一併帶走。 |

已上螢幕並讓時長自然走完，或由使用者與 `Dismiss()` 關掉的通知，會以 `NotificationResult.Success` 完成——那些是結局，不是失敗。

## 6. 點擊回呼

`OnClick` 會在**任何由使用者發起的關閉**時執行，而程式碼把這些情況當成同一種結果：點卡片本體與點 ✕ 走的是同一路徑。倒數單純到期**不會**呼叫它，§5 中的任何失敗也不會。

回呼是外掛模組自己的程式碼，因此其中的例外會被捕捉、附帶來源名稱記錄，而且絕不會被帶回啟動器的輸入管線。工作本身有可能失敗的話，請放在回呼之外執行；回呼的職責是把使用者帶到你的視窗（隨包的範例是 `Plugins/Calendar`，它就是用這個回呼啟用日曆檢視）。

## 7. 外掛模組無法偽造的來源歸屬

卡片上印出的名稱來自 `Assembly.GetCallingAssembly()`，在該服務門面的每一個**公用**框架中讀取，再由宿主對應：名為 `Lertaro.App*` 的組件顯示為 `Lertaro`，已註冊的外掛模組以其託管顯示名稱呈現，其他一律退回組件名稱並移除 `Lertaro.Plugins.` 前綴。以字串傳入來源名稱這個選項是刻意不提供——那是唯一一項外掛模組不該自己選擇的歸屬。

## 8. 放置與繪製載體

- **哪一螢幕**：前景視窗所在的螢幕，沒有則取滑鼠指標所在，再退回主螢幕。工作區會透過該螢幕的 DPI 轉換為 DIP，因此卡片在 200% 縮放的面板上與在 100% 面板上具有相同的實體尺寸。
- **堆疊順序**：右下角，依抵達先後排序；被移除卡片上方的卡片會重新滑動（這是唯一的動畫，約 200 ms，並伴隨 40 DIP 的進場落下）。
- **尺寸**：卡片高度最多是工作區的一半（`min(260 DIP, 50 %)`），因此過長的信息吃不掉一台小螢幕。
- **拖曳**：卡片只能由它的標題列拖曳，而被拖過的卡片會留在自己的角落。顯示設定變更會讓堆疊重新錨定，使用者的拖曳位置因此被捨棄——這比讓卡片跑到螢幕外更划算。
- **繪製載體類型**：在建構時依作用中主題的 `WindowOpacity` 一次決定。完全不透明的主題取得由視窗管理器負責圓角的普通視窗，因而保留 ClearType；半透明主題則取得分層視窗，其圓角必須改為自行繪製與裁剪。跨越主題切換而顯示的卡片，因此會保持它開始時的那種載體直到消失為止。
- **Topmost** 永遠開啟，而且這些視窗被排除在 Alt+Tab 之外（`WS_EX_TOOLWINDOW`）。通知不做淡入淡出：沒有任何程式碼動畫化 `Window.Opacity`，因為那會付出 ClearType 與每幀逐像素合成的代價。到來的訊號，是在一枚已經完全不透明的視窗內部閃過的一圈邊框光芒（約 750 ms）。

## 9. 執行緒與生命週期

- 從任何執行緒呼叫皆可。宿主會把每個通知入口串行化在同一把鎖上，因此 `Show` 是一次簡短的阻塞呼叫——不要在逐項處理的熱迴圈裡驅動它，改送一張摘要卡片。
- 視窗以 `DispatcherPriority.Background` 呈現，因此通知批次不會餓死啟動器自己的輸入工作。
- **工作階段鎖定**期間（螢幕保護程式、`Win+L`），倒數會被凍結、卡片會被隱藏；工作階段解鎖後它們會帶著原本獲得的時間回來。
- 停用或卸載外掛模組會取消它顯示中*與*佇列中的卡片（`CancelledByPluginUnload`）；啟動器的有序關機對所有尚未結束的通知做同樣的事（`HostShuttingDown`）。
- 這個版本**沒有任何使用者可見的通知設定**：沒有勿打擾模式、沒有外掛模組個別開關，而上述位置與時長規則是宿主的，無法設定。設計請求時請據此考量——需要頻繁開口的外掛模組應該自己安靜下來，而不是請使用者把它靜音。

## 10. 範例

```csharp
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

// 每個提醒一張卡片，只要它還在螢幕上就被取代，而不是重複出現。
var handle = PluginNotificationService.Show(new NotificationRequest
{
    Id = $"reminder-{due.Item.Id}",
    Title = TranslationService.Get("Reminder_Title"),
    Message = due.Item.Text,
    Level = NotificationLevel.Warn,
    DurationSeconds = 12,               // 由宿主裁剪進 2..30 之內
    OnClick = () => CalendarView.ShowOrActivate(),
});

// 只在真正需要結束狀態的地方 await；這個工作一定會完成。
_ = handle.Completion.ContinueWith(t =>
{
    if (t.Result.Failure is NotificationFailure.Replaced)
        Logger.Log($"Superseded by a newer reminder: {due.Item.Id}", LogLevel.Debug);
});
```

> [!NOTE]
> 本頁的簽章、預設值與限制均對照 `PluginSdk/Abstractions/NotificationRequest.cs`、`PluginSdk/Abstractions/INotificationHandle.cs`、`PluginSdk/Services/PluginNotificationService.cs`，以及宿主端 `App/Services/Notifications/` 與 `App/Views/Notifications/` 下的程式碼讀取校對。
