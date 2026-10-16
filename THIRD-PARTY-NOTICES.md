# Componenti e dati di terze parti

Questo documento elenca i componenti, le icone e i dati di terze parti distribuiti con WhoIs Pro, con le loro licenze.
Il setup e il pacchetto portabile lo copiano accanto al programma. Va aggiornato quando cambiano le versioni dei
pacchetti o le fonti dei dati.

## .NET 10 e Windows Forms

- Uso: il programma pubblicato (`Scripts\Publish.ps1`) è autonomo e contiene il runtime di .NET e di Windows Forms.
- Progetto: [.NET](https://dotnet.microsoft.com/), © .NET Foundation e collaboratori.
- Licenza: [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT); i componenti di terze parti del runtime sono
  elencati nei suoi file `THIRD-PARTY-NOTICES.TXT`
  ([runtime](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT),
  [Windows Forms](https://github.com/dotnet/winforms/blob/main/THIRD-PARTY-NOTICES.TXT)).

## Microsoft WebView2 SDK 1.0.4078.44

- Uso: la finestra del programma è una pagina HTML mostrata con WebView2 (`Microsoft.Web.WebView2.Core.dll`,
  `Microsoft.Web.WebView2.WinForms.dll`, `WebView2Loader.dll`).
- Pacchetto: [Microsoft.Web.WebView2 1.0.4078.44](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4078.44).
- Il runtime di WebView2 (Microsoft Edge WebView2 Runtime) non è incluso: è quello già installato in Windows, alle
  condizioni di Microsoft.
- Licenza del pacchetto:

```
Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors 
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Icone Lucide

- Uso: le icone a tratto dell'interfaccia (`ui\core\icons.js`) usano i tracciati di [Lucide](https://lucide.dev/); le
  icone derivate dal progetto Feather (elencate nel file
  [LICENSE](https://github.com/lucide-icons/lucide/blob/main/LICENSE) di Lucide) restano con la licenza MIT di Feather.
  Il logo e le immagini della cartella `Assets` sono del progetto.
- Licenza di Lucide:

```
ISC License

Copyright (c) 2026 Lucide Icons and Contributors

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

- Licenza di Feather, per le icone derivate da Feather:

```
The MIT License (MIT)

Copyright (c) 2013-present Cole Bemis

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Produttori dei dispositivi di rete

- `manuf.txt`, accanto al programma, è il file «manuf» pubblicato dal progetto [Wireshark](https://www.wireshark.org/)
  (https://www.wireshark.org/download/automated/data/manuf), incluso così com'è con la sua intestazione. Wireshark lo
  compila dai registri pubblici dell'IEEE; il progetto Wireshark è distribuito con la licenza
  [GNU GPL versione 2 o successiva](https://www.wireshark.org/about.html).
- *Aggiorna dall'IEEE* scarica i registri MA-L, MA-M e MA-S dell'[IEEE Registration
  Authority](https://standards.ieee.org/products-programs/regauth/), consultabili gratuitamente, e li salva sul PC
  nello stesso formato.
- I dati sono forniti così come sono, senza garanzie di accuratezza o aggiornamento.

## Servizi esterni

Il programma non usa servizi dell'autore. Si collega a GitHub per controllare e scaricare gli aggiornamenti
(`api.github.com`, `github.com`), all'IEEE o a Wireshark solo quando aggiorni il database dei produttori e ai server
WHOIS, RDAP e DNS che interroghi (per esempio `whois.iana.org` e i registri regionali), alle loro condizioni.

## Installazione

Il programma di installazione (`Setup\WhoIsPro.iss`) è creato con [Inno Setup](https://jrsoftware.org/isinfo.php),
© 1997-2026 Jordan Russell, parti © 2000-2026 Martijn Laan ([licenza](https://jrsoftware.org/files/is/license.txt)).
