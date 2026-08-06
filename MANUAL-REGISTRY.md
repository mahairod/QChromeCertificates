# Ручная настройка политик через реестр

Того же результата можно добиться без запуска Chrome Certificate Policy Manager. Для этого нужно создать две политики браузера в реестре Windows.

Политика `CACertificatesWithConstraints` поддерживается Chrome и Chromium начиная с версии 132, а Microsoft Edge — с версии 133. Поддержку в конкретной сборке Brave следует проверять на `brave://policy/`. Политика добавляет публичный CA-сертификат непосредственно в браузер и не устанавливает его в хранилище сертификатов Windows.

## Пути в реестре

Для текущего пользователя:

```text
Google Chrome:  HKEY_CURRENT_USER\Software\Policies\Google\Chrome
Microsoft Edge: HKEY_CURRENT_USER\Software\Policies\Microsoft\Edge
Brave:          HKEY_CURRENT_USER\Software\Policies\BraveSoftware\Brave
Chromium:       HKEY_CURRENT_USER\Software\Policies\Chromium
```

Для всех пользователей компьютера можно использовать соответствующие ключи в `HKEY_LOCAL_MACHINE`. В этом случае требуются права администратора.

Настраиваются два значения:

| Имя | Тип | Значение |
| --- | --- | --- |
| `CAPlatformIntegrationEnabled` | `REG_DWORD` | `0` — не использовать добавленные пользователем CA из хранилища Windows; `1` — использовать |
| `CACertificatesWithConstraints` | `REG_SZ` | JSON-массив сертификатов и их ограничений |

Перед изменением реестра экспортируйте существующий ключ через редактор реестра или командой:

```powershell
reg.exe export "HKCU\Software\Policies\Google\Chrome" chrome-policies.reg
```

Для другого браузера замените путь на соответствующий путь из таблицы выше.

## Формат CACertificatesWithConstraints

Значение `CACertificatesWithConstraints` содержит JSON такого вида:

```json
[
  {
    "certificate": "BASE64_DER_CERTIFICATE",
    "constraints": {
      "permitted_dns_names": [
        "gosuslugi.ru",
        ".gosuslugi.ru"
      ],
      "permitted_cidrs": [
        "10.1.1.0/24"
      ]
    }
  }
]
```

`certificate` — полный публичный X.509-сертификат в DER-формате, закодированный Base64. Закрытого ключа в политике нет.

Для каждого сертификата должно быть задано хотя бы одно непустое ограничение:

- `permitted_dns_names` разрешает DNS-имена из запроса на проверку сертификата;
- `permitted_cidrs` разрешает IP-имена из запроса на проверку сертификата в формате IPv4/IPv6 CIDR; это относится прежде всего к прямым обращениям вида `https://192.0.2.1`, а не к IP-адресу, в который разрешился домен;
- имя без начальной точки относится к самому домену;
- имя с начальной точкой относится к его поддоменам, поэтому для домена и поддоменов обычно нужны обе записи.

Важно: если определённый тип ограничения отсутствует, браузер разрешает для него любые имена этого типа. Например, сертификат только с `permitted_dns_names` не ограничен для прямых URL с IP-адресом и подходящим IP SAN сертификата. `permitted_cidrs` не является сетевым фильтром для адресов, в которые разрешаются домены.

Для нескольких сертификатов добавьте в верхний JSON-массив несколько объектов.

## Настройка через редактор реестра

1. Откройте `regedit.exe` от имени пользователя, для которого настраивается браузер.
2. Перейдите в ключ выбранного браузера, указанный выше. Создайте отсутствующие разделы.
3. Создайте параметр `CAPlatformIntegrationEnabled` типа `DWORD (32 бита)` со значением `0` или `1`.
4. Создайте строковый параметр `CACertificatesWithConstraints` и вставьте в него JSON одной строкой.
5. Откройте страницу политик выбранного браузера и нажмите кнопку повторной загрузки: `chrome://policy/`, `edge://policy/` или `brave://policy/`.
6. Убедитесь, что обе политики отображаются без ошибок.

## Настройка через PowerShell

Следующий пример читает сертификат из файла, преобразует его в DER и записывает политики для текущего пользователя Google Chrome:

```powershell
$certificatePath = 'C:\Certificates\root-ca.cer'
$policyPath = 'HKCU:\Software\Policies\Google\Chrome'

$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
$certificateBase64 = [Convert]::ToBase64String($certificate.RawData)

$entries = @(
    [ordered]@{
        certificate = $certificateBase64
        constraints = [ordered]@{
            permitted_dns_names = @(
                'gosuslugi.ru'
                '.gosuslugi.ru'
            )
            permitted_cidrs = @(
                '10.1.1.0/24'
            )
        }
    }
)

$json = ConvertTo-Json -InputObject $entries -Depth 5 -Compress

New-Item -Path $policyPath -Force | Out-Null
New-ItemProperty -Path $policyPath -Name CAPlatformIntegrationEnabled -PropertyType DWord -Value 0 -Force | Out-Null
New-ItemProperty -Path $policyPath -Name CACertificatesWithConstraints -PropertyType String -Value $json -Force | Out-Null
```

Для другого браузера измените `$policyPath`:

```powershell
# Microsoft Edge
$policyPath = 'HKCU:\Software\Policies\Microsoft\Edge'

# Brave
$policyPath = 'HKCU:\Software\Policies\BraveSoftware\Brave'

# Chromium
$policyPath = 'HKCU:\Software\Policies\Chromium'
```

Если в сертификате нужны только DNS- или только CIDR-ограничения, удалите ненужное поле из объекта `constraints`, учитывая описанное выше поведение отсутствующего типа ограничения.

## Удаление политик

Удаляйте только созданные значения, а не весь раздел политик браузера:

```powershell
$policyPath = 'HKCU:\Software\Policies\Google\Chrome'
Remove-ItemProperty -Path $policyPath -Name CAPlatformIntegrationEnabled -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $policyPath -Name CACertificatesWithConstraints -ErrorAction SilentlyContinue
```

После удаления повторно загрузите политики на странице политик выбранного браузера.

## Документация

- [CACertificatesWithConstraints](https://chromeenterprise.google/policies/ca-certificates-with-constraints/)
- [CAPlatformIntegrationEnabled](https://chromeenterprise.google/policies/ca-platform-integration-enabled/)
- [Политики управления сертификатами Microsoft Edge](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/cacertificateswithconstraints)
- [Групповые политики Brave](https://support.brave.com/hc/en-us/articles/4410256274317-%E3%82%B0%E3%83%AB%E3%83%BC%E3%83%97%E3%83%9D%E3%83%AA%E3%82%B7%E3%83%BC)
- [Пути политик Chrome и Chromium в Windows](https://www.chromium.org/administrators/policy-templates/)
- [Сложные политики в реестре Windows](https://www.chromium.org/administrators/complex-policies-on-windows/)
- [Схема CACertificatesWithConstraints в Chromium](https://chromium.googlesource.com/chromium/src/+/HEAD/components/policy/resources/templates/policy_definitions/CertificateManagement/CACertificatesWithConstraints.yaml)
