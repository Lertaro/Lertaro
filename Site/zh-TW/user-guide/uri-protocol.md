# URI 通訊協定（lertaro://）

Lertaro 會自動把自己註冊為 `lertaro://` 連結的處理常式——不需要額外的安裝步驟，第一次執行時就會自動完成註冊。這樣任何能開啟連結的東西（瀏覽器、捷徑、別的程式、指令碼）都能直接跳到 Lertaro
的某個具體位置，而不是只能靠熱鍵觸發。

如果 Lertaro 還沒執行，開啟一個 `lertaro://` 連結會先啟動它，再執行這個連結指向的操作。如果已經在執行，正在執行的那個執行個體會直接處理這個連結——不會再啟動第二份處理程序。

## 支援的連結

| 連結 | 作用 |
|---|---|
| `lertaro://` | 啟用快速搜尋視窗——效果和用熱鍵叫出它一樣。 |
| `lertaro://search/[關鍵字]` | 啟用快速搜尋視窗，並預先帶入 `[關鍵字]`。 |
| `lertaro://fullsearch/[關鍵字]` | 開啟完整搜尋視窗，並預先帶入 `[關鍵字]`。 |
| `lertaro://settings/page/[分區]` | 開啟設定視窗，並切到指定的頂層分區。 |
| `lertaro://settings/entry/[序號]` | 開啟設定視窗，並直接跳轉到某一項具體設定，並高亮顯示。 |
| `lertaro://localsend` | 開啟空白的 LocalSend 傳送視窗。 |
| `lertaro://localsend/items?path=[編碼後的路徑]` | 向 LocalSend 加入一個或多個檔案或資料夾；多個項目應重複使用 `path` 參數。 |
| `lertaro://localsend/text?value=[編碼後的文字]` | 向 LocalSend 加入文字。 |

```
lertaro://search/report
lertaro://settings/page/Appearance
```

第一個會啟用快速搜尋視窗，並已經用「report」篩選好；第二個會直接開啟設定視窗的「外觀」頁。

`[分區]` 對應側邊欄頂層的某一項：`Service`、`Index`、`General`、`Appearance`、`Hotkeys`、
`Plugins`、`Favorites`、`History`、`QuickPanel`、`About`——不區分大小寫。

`[序號]` 不是給人手動輸入用的——它是[設定搜尋](./instant-answers)在你選中某項設定結果時自己產生的一個數字，選中結果會自動帶上這個序號，原樣跳轉回那一項設定。這個序號在重新啟動之間並不穩定，不要指望某個具體數字每次都對應同一項設定。

## LocalSend 連結

每個檔案路徑或文字值都必須經過 URL 編碼。加入多個檔案或資料夾時，應重複使用 `path` 參數；所有路徑都必須是已經存在的絕對路徑。例如：

```
lertaro://localsend/items?path=C%3A%5CUsers%5Ctestuser%5CDesktop%5Ca.txt&path=D%3A%5CShared%5Cb.txt
lertaro://localsend/text?value=Hello%20world
```

LocalSend 連結只會填入傳送視窗，絕不會自動選擇裝置或開始傳輸。如果 LocalSend 尚未啟用，Lertaro 會改為開啟 LocalSend 設定頁。參數無效、混用或過長時，整個要求都會被忽略。

## 無法識別的連結

任何比對不上已知路由的連結——打錯了字、分區不存在、`lertaro://` 後面跟了一堆亂七八糟的內容——都會被直接忽略。由於任何網站或程式都能在你不知情的情況下叫用這個通訊協定，一個錯誤或異常的連結不應該產生任何出人意料的效果；這類情況只會記進記錄方便你自己排查，除此之外什麼都不會發生。
