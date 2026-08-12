# URI 協議（lertaro://）

Lertaro 會自動把自己註冊為 `lertaro://` 鏈接的處理程式——不需要額外的安裝步驟，第一次運行時就會自動完成註冊。這樣任何能打開鏈接的東西（瀏覽器、捷徑、別的程式、腳本）都能直接跳到 Lertaro
的某個具體位置，而不是只能靠熱鍵觸發。

如果 Lertaro 還沒運行，打開一個 `lertaro://` 鏈接會先啓動它，再執行這個鏈接指向的操作。如果已經在運行，正在運行的那個實例會直接處理這個鏈接——不會再啓動第二份處理程序。

## 支援的鏈接

| 鏈接 | 作用 |
|---|---|
| `lertaro://` | 激活快速搜尋視窗——效果和用熱鍵呼出它一樣。 |
| `lertaro://search/[關鍵詞]` | 激活快速搜尋視窗，並預填 `[關鍵詞]`。 |
| `lertaro://fullsearch/[關鍵詞]` | 打開完整搜尋視窗，並預填 `[關鍵詞]`。 |
| `lertaro://settings/page/[分區]` | 打開設定視窗，並切到指定的頂層分區。 |
| `lertaro://settings/entry/[序號]` | 打開設定視窗，並直接跳轉到某一項具體設定，並高亮顯示。 |
| `lertaro://localsend` | 打開空白的 LocalSend 發送視窗。 |
| `lertaro://localsend/items[/編碼後的項目...]` | 切換到文件/資料夾模式，並可按路徑段添加一個或多個項目。 |
| `lertaro://localsend/text[/編碼後的文字]` | 切換到文字模式，並可填入編碼後的文字。 |

```
lertaro://search/report
lertaro://settings/page/Appearance
```

第一個會激活快速搜尋視窗，並已經用"report"過濾好；第二個會直接打開設定視窗的"外觀"頁。

`[分區]` 對應側邊欄頂層的某一項：`Service`、`Index`、`General`、`Appearance`、`Hotkeys`、
`Plugins`、`Favorites`、`History`、`QuickPanel`、`About`——不區分大小寫。

`[序號]` 不是給人手動輸入用的——它是[設定搜尋](./instant-answers)在你選中某項設定結果時自己生成的一個數字，選中結果會自動帶上這個序號，原樣跳轉回那一項設定。這個序號在重啓之間並不穩定，不要指望某個具體數字每次都對應同一項設定。

## LocalSend 鏈接

每個文件/資料夾路徑或文字值都必須整體編碼為一個 URL 路徑段。添加多個項目時，為每個項目追加一個獨立的編碼路徑段；所有路徑都必須是已經存在的絕對路徑。例如：

```
lertaro://localsend/items/C%3A%5CUsers%5Ctestuser%5CDesktop%5Ca.txt/D%3A%5CShared
lertaro://localsend/text/Hello%20world
```

`lertaro://localsend/items` 會打開收集頁並切到文件/資料夾模式，`lertaro://localsend/text` 則切到文字模式。帶有內容的鏈接會繼續進入裝置選擇，但絕不會自動選擇裝置或開始傳輸。如果發送視窗已經打開，這個鏈接不會執行任何操作，也不會改變視窗目前的內容或狀態。如果 LocalSend 尚未啓用，Lertaro 會改為打開 LocalSend 設定頁。內容無效或過長時，整個請求都會被忽略。

## 無法識別的鏈接

任何匹配不上已知路由的鏈接——打錯了字、分區不存在、`lertaro://` 後面跟了一堆亂七八糟的內容——都會被直接忽略。由於任何網站或程式都能在你不知情的情況下調起這個協議，一個錯誤或異常的鏈接不應該產生任何出人意料的效果；這類情況只會記進日誌方便你自己排查，除此之外什麼都不會發生。
