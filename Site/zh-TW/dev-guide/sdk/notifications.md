# 通知

`Lertaro.PluginSdk.Services.PluginNotificationService` 讓外掛模組能透過宿主自己的視窗，從背景取得使用者的注意——不必另開程序，不依賴系統 toast 管道，也不與宿主 UI 組件產生耦合。畫面上任何東西停留多久、出現在何處，都由宿主而非外掛模組決定。

## 1. 進入點

| 簽章 | 返回 | 使用時機 |
| :--- | :--- | :--- |
| `INotificationHandle Show(NotificationRequest request)` | 立即取得一個句柄 | 你之後想取代或取消這張通知 |
| `Task<NotificationResult> ShowAsync(NotificationRequest request)` | 結束狀態 | 你只關心它如何結束 |
| `bool Show(string title, string text, Action? onClick = null)` | 是否有宿主受理 | 兩參數的舊式形態；回傳 `false` 表示沒有宿主接線，因此仍可走備援路徑 |

每個進入點從外掛模組自己的背景執行緒呼叫都安全，而且**絕不拋出例外**——一個逃出的例外會連同呼叫端的迴圈一起摧毀。當完全沒有指派宿主委派時（也就是在啟動器之外執行的外掛模組），該呼叫就是一次安靜的空操作，答案是帶著 `Unavailable` 的已完成句柄；只有在宿主自己的委派拋出例外時，請求才會以 `Warn` 層級記錄。

`INotificationHandle` 恰好只有兩個成員：`Task<NotificationResult> Completion` 與 `void Dismiss()`。`Completion` **總是**會完成，包括什麼都沒顯示到畫面上的情況，因此對它的 `await` 不會讓執行緒停擺。`Dismiss()` 會像使用者手動關閉一樣關掉一張已顯示的卡片，一旦通知已消失它就變成空操作，而且永遠不會逆轉一個已交付的結果。

需要自己擁有視窗的外掛模組請改用 [`Windows.PluginWindow`](./services)；需要的是*一個回答*而非一個公告的外掛模組請改用 `PluginMessageBoxService`。

## 2. `NotificationRequest`

| 屬性 | 預設值 | 行為 |
| :--- | :--- | :--- |
| `string? Id` | `null` | 兩個具有相同 `Id` 的請求指向同一個通知。見**依 Id 取代**（§4）。 |
| `string Title` | `string.Empty` | 卡片標題。`BottomNotice` 會忽略它，因為它沒有標題列。 |
| `string Message` | `string.Empty` | 內文。`Title` 與 `Message` 兩者皆為空白或只含空白字元時，該請求會被當作 `InvalidRequest` 拒絕——沒有東西可顯示。 |
| `NotificationLevel Level` | `Info` | `Info` / `Warn` / `Error`。只決定圖示與語意顏色，別無其他。 |
| `NotificationPosition Position` | `CardStack` | `CardStack` 或 `BottomNotice`。各有自己的佇列與自己的規則；彼此從不互相阻擋。 |
| `double? DurationSeconds` | `null` | `null` 採用該位置自己的預設值。超出該位置範圍的明確值會被**裁剪到最近的邊界**，絕不被拒絕；`NaN` 或負值會落在較短的一端。 |
| `Action? OnClick` | `null` | 在關閉之外另行執行。見**點擊回呼**（§6）。 |

## 3. 兩個位置

| | `CardStack`（右下角） | `BottomNotice`（下方置中） |
| :--- | :--- | :--- |
| 形態 | 帶標題、來源名稱、✕ 與「全部標為已讀」的卡片 | 一行；無標題、無來源、無控制項 |
| 同時數量 | 5 張可見，其餘排隊 | 1——新的一張會立即取代目前那張，無需等待任何拆毀 |
| 預設時長 | 8 秒 | 4 秒 |
| 裁剪範圍 | 2 – 30 秒 | 2 – 10 秒 |
| `OnClick` | 會履行 | 忽略（沒有任何可互動元素） |
| 全螢幕應用佔用螢幕時 | 收合進提示行，上限 5 秒 | 不受影響 |

當你要發出一波通知時，還有兩個節律數字值得一併留意：

- 宿主執行**一個 100 ms 的 tick**，而它在每個 tick 最多提升**一張**正在排隊的卡片。因此一波通知會自動拉開彼此的到來時間；而由於每張卡片從它出現的那一刻開始計算自己的時間，拉開到來的間隔也就會拉開到期的間隔——一波通知不再一次從堆疊中奪走五個位置。
- **單一外掛模組最多可有 10 個被受理的卡片請求**（在畫面上*加*排隊中的合計）。第十一個會被當成 `QueueFull` 丟棄並記錄。這個上限只在卡片的准入路徑上強制執行：`BottomNotice` 請求絕不會被計入，因為提示行是單一位置、只會覆寫自己。跨外掛模組之間沒有全域的待定上限，因此一個外掛模組不會被另一個的一波通知餓死，但同樣地，一個失控的產生者在全域層面也不會受限。

## 4. 依 Id 取代

給請求一個 `Id`，就是讓一個重複的狀態更新不致於用自身的拷貝塞滿畫面的做法：一個 `Id` 與某張**可見**卡片相符的新請求，會就地覆寫該卡片——在堆疊中佔同一個位置，而不是跳到最前面——而較早呼叫端的句柄會以 `Replaced` 完成。那些需要知道自己被取代的呼叫端，正是會保留句柄的那些。

一個必須尊重的限制：去重是對目前畫面上的卡片比對，**不是**對仍在佇列中排隊的卡片比對。在畫面已滿時連續發送同一個 `Id` 的五份拷貝的外掛模組，會讓這五份全部排隊。

## 5. 結果與失敗原因

`NotificationResult` 是 `Succeeded`，加上一個選填的 `NotificationFailure`。第一個抵達某項目的結束狀態取勝；此後沒有任何東西會改寫它。

| `NotificationFailure` | 時機 |
| :--- | :--- |
| `Unavailable` | 沒有宿主服務該請求——在啟動器之外執行的外掛模組，或拋出例外的宿主委派。 |
| `InvalidRequest` | `Title` 與 `Message` 皆為空白或只含空白字元。 |
| `QueueFull` | 該外掛模組已有 10 個卡片請求被受理。 |
| `Replaced` | 較新的請求帶著相同的 `Id`。一次 `BottomNotice` 的覆寫會讓被取代的請求以這個方式完成，**即使兩者都沒帶有 `Id`**，因為提示行是單一位置、永遠都會覆寫。 |
| `CancelledByPluginUnload` | 卡片正在顯示或排隊時，外掛模組被停用或卸載。 |
| `HostShuttingDown` | 啟動器正在關閉，並把通知一併帶走。 |

一張已抵達畫面並用盡其時間，或被使用者或被 `Dismiss()` 關掉的通知，會以 `NotificationResult.Success` 完成——那些是結束，不是失敗。

## 6. 點擊回呼

`OnClick` 會在**任何使用者關閉**時執行，程式碼將它視為同一種結果：點擊卡片主體與點擊 ✕ 走同一條路。單純到期的倒數**不會**執行它，§5 中的任何失敗也不會。

這個回呼是外掛模組的程式碼，因此內部的例外會被捕獲、連同來源名稱一起記錄，且絕不會被帶回啟動器的輸入管線。如果工作本身可能失敗，就把工作放到回呼之外去做；回呼的職責是把使用者帶到你的視窗（倉庫內的範例是 `Plugins/Calendar`，它用它來啟用日曆檢視）。

## 7. 外掛模組無法偽造的來源標記

卡片上列印出的名稱來自 `Assembly.GetCallingAssembly()`，在外表的每個**公用**框架中讀取，再由宿主對應：名為 `Lertaro.App*` 的組件顯示為 `Lertaro`，已註冊的外掛模組以其管理的顯示名稱顯示，其餘的則退回組件名稱並去掉 `Lertaro.Plugins.` 前綴。刻意不提供以字串傳入來源名稱——那正是外掛模組絕對不該自行選擇的來源標記。

## 8. 位置與表面

- **在哪個螢幕**：前景視窗所在的螢幕，若無則為游標所在的螢幕，再無則為主要螢幕。工作區域會透過該監視器的 DPI 轉換為 DIP，因此卡片在 200 % 縮放的面板上與在 100 % 的面板上是相同的實體大小。
- **堆疊順序**：右下角，依到達順序排列；移除某張之後，它上方的卡片會重新滑動（這是唯一的動畫，約 200 ms，帶 40 DIP 的進場下滑）。
- **大小**：一張卡片最高為工作區域高度的一半（`min(260 DIP, 50 %)`），因此一段長訊息無法吃掉一個小螢幕。
- **拖曳**：卡片只能透過其標題列拖曳，而被拖曳的卡片會保有自己的角落。顯示設定的變更會重新錨定堆疊，這會讓使用者的一次拖曳作廢——比起讓卡片跑到螢幕外，這是較便宜的取捨。
- **表面種類**：在建立時依作用中主題的 `WindowOpacity` 一次決定。完全不透明的主題得到一個由視窗管理員圓角的普通視窗，從而保留 ClearType；半透明的主題則得到一個階層式視窗，其圓角必須改為繪製並裁剪。因此跨過一次主題切換而顯示的卡片，會一直保持它開始時的那種種類，直到它消失。
- **Topmost** 永遠開啟，而這些視窗被排除在 Alt+Tab 之外（`WS_EX_TOOLWINDOW`）。通知不會淡出：沒有東西會對 `Window.Opacity` 做動畫，因為那會在每一幀犧牲 ClearType 與一次逐像素合成。到來的標記是一個已經完全不透明的視窗內部的一次邊框閃光（約 750 ms）。

## 9. 執行緒與生命週期

- 從任何執行緒呼叫。宿主會在單一閘門上序列化每個通知進入點，所以 `Show` 是一個簡短、阻塞的呼叫——不要從逐項的熱迴圈驅動它；改發一張摘要卡片即可。
- 視窗以 `DispatcherPriority.Background` 呈現，因此一波通知不會讓啟動器自己的輸入工作挨餓。
- 當**工作階段被鎖定**（螢幕保護程式、`Win+L`）時，倒數會被凍結、卡片會被隱藏；工作階段解鎖時它們會回來，並仍帶著當初給它們的時間。
- 停用或卸載外掛模組會取消它正在顯示*以及*排隊中的卡片（`CancelledByPluginUnload`）；啟動器的正常關閉會對所有未結的通知做同樣的事（`HostShuttingDown`）。
- 這個版本**沒有任何面向使用者的通知設定**：沒有勿擾模式、沒有逐外掛模組開關，而上述的位置與時長規則是宿主的、不可設定。據此設計你的請求——一個不停說話的外掛模組應該讓它自己安靜下來，而不是要使用者把它靜音。

## 10. 範例

```csharp
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

// 每個提醒一張卡片，若它仍在畫面上則取代而非重複。
var handle = PluginNotificationService.Show(new NotificationRequest
{
    Id = $"reminder-{due.Item.Id}",
    Title = TranslationService.Get("Reminder_Title"),
    Message = due.Item.Text,
    Level = NotificationLevel.Warn,
    DurationSeconds = 12,               // 由宿主裁剪進 2..30
    OnClick = () => CalendarView.ShowOrActivate(),
});

// 只在你真的需要結束狀態的地方 await 它；這個工作總是會完成。
_ = handle.Completion.ContinueWith(t =>
{
    if (t.Result.Failure is NotificationFailure.Replaced)
        Logger.Log($"Superseded by a newer reminder: {due.Item.Id}", LogLevel.Debug);
});
```

> [!NOTE]
> 本頁的簽章、預設值與上限，是對照 `PluginSdk/Abstractions/NotificationRequest.cs`、`PluginSdk/Abstractions/INotificationHandle.cs`、`PluginSdk/Services/PluginNotificationService.cs`，以及 `App/Services/Notifications/` 與 `App/Views/Notifications/` 之下的宿主端原始碼讀取而得。
