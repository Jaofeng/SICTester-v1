# SICTester-v1
台中捷運綠線列車通訊服務控制器測具程式-樹莓版

使用 Win10 IoT 為 OS，並需針對以下幾點進行設定：
<ol>
  <li>樹莓板 Ethernet 網卡 IP 需設定為 192.168.127.x</li>
  <li>部署至樹莓上後，需將之設定為 Defaut APP</li>
</ol>

程式說明：
<ol>
  <li>SICTester 以 Modbus TCP 的方式與 SIC 連線並取得資料</li>
  <li>IoTModbus 由 NModbus4 v1.11 改版而來，目前僅支援 TCP 連線方式</li>
</ol>
