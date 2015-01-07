﻿namespace Suave.Utils

/// Small crypto module that can do HMACs and generate random strings to use
/// as keys, as well as create a 'cryptobox'; i.e. a AES256+HMACSHA256 box with
/// compressed plaintext contents so that they can be easily stored in cookies.
module Crypto =
  open System
  open System.IO
  open System.IO.Compression
  open System.Text
  open System.Security.Cryptography

  /// The default hmac algorithm
  [<Literal>]
  let HMACAlgorithm = "HMACSHA256"

  /// The length of the HMAC value in number of bytes
  [<Literal>]
  let HMACLength = 32 // = 256 / 8

  /// Calculate the HMAC of the passed data given a private key
  let hmac (key : byte []) offset count (data : byte[]) =
    use hmac = HMAC.Create(HMACAlgorithm)
    hmac.Key <- key
    hmac.ComputeHash (data, offset, count)

  let hmac' key (data : byte []) =
    hmac key 0 (data.Length) data

  /// Calculate the HMAC value given the key
  /// and a seq of string-data which will be concatenated in its order and hmac-ed.
  let hmac'' (key : byte []) (data : string seq) =
    hmac' key (String.Concat data |> UTF8.bytes)

  /// # bits in key
  let KeySize   = 256

  /// # bytes in key
  let KeyLength = KeySize / 8

  /// # bits in block
  let BlockSize = 128

  /// # bytes in IV
  /// 16 bytes for 128 bit blocks
  let IVLength = BlockSize / 8

  /// the global crypto-random pool for uniform and therefore cryptographically
  /// secure random values
  let crypt_random = RandomNumberGenerator.Create()

  /// Fills the passed array with random bytes
  let randomize (bytes : byte []) =
    crypt_random.GetBytes bytes
    bytes

  /// Generates a string key from the available characters with the given key size.
  let generate_key key_length =
    Array.zeroCreate<byte> key_length |> randomize

  let generate_key' () =
    generate_key KeyLength

  let generate_iv iv_length =
    Array.zeroCreate<byte> iv_length |> randomize

  let generate_iv' () =
    generate_iv IVLength

  /// key: 32 bytes for 256 bit key
  /// Returns a new key and a new iv as two byte arrays as a tuple.
  let generate_keys () =
    generate_key' (), generate_iv' ()

  type SecretboxEncryptionError =
    | InvalidKeyLength of string
    | EmptyMessageGiven

  type SecretboxDecryptionError =
    | TruncatedMessage of string
    | AlteredOrCorruptMessage of string

  let private secretbox_init key iv =
    let aes = new AesManaged()
    aes.KeySize   <- KeySize
    aes.BlockSize <- BlockSize
    aes.Mode      <- CipherMode.CBC
    aes.Padding   <- PaddingMode.PKCS7
    aes.IV        <- iv
    aes.Key       <- key
    aes

  let secretbox (key : byte []) (msg : byte []) =
    if key.Length <> KeyLength then
      Choice2Of2 (InvalidKeyLength (sprintf "key should be %d bytes but was %d bytes" KeyLength (key.Length)))
    elif msg.Length = 0 then
      Choice2Of2 EmptyMessageGiven
    else
      let iv  = generate_iv' ()
      use aes = secretbox_init key iv

      let mk_cipher_text (msg : byte []) (key : byte []) (iv : byte []) =
        use enc      = aes.CreateEncryptor(key, iv)
        use cipher   = new MemoryStream()
        use crypto   = new CryptoStream(cipher, enc, CryptoStreamMode.Write)
        let bytes = msg |> Encoding.gzip_encode
        crypto.Write (bytes, 0, bytes.Length)
        crypto.FlushFinalBlock()
        cipher.ToArray()

      use cipher_text = new MemoryStream()

      let bw  = new BinaryWriter(cipher_text)
      bw.Write iv
      bw.Write (mk_cipher_text msg key iv)
      bw.Flush ()

      let hmac = hmac' key (cipher_text.ToArray())
      bw.Write hmac
      bw.Dispose()

      Choice1Of2 (cipher_text.ToArray())

  let secretbox' (key : byte []) (msg : string) =
    secretbox key (msg |> UTF8.bytes)

  let secretbox_open (key : byte []) (cipher_text : byte []) =
    let hmac_calc = hmac key 0 (cipher_text.Length - HMACLength) cipher_text
    let hmac_given = Array.zeroCreate<byte> HMACLength
    Array.blit cipher_text (cipher_text.Length - HMACLength) // from
               hmac_given  0                                 // to
               HMACLength                                    // # bytes for hmac

    if cipher_text.Length < HMACLength + IVLength then
      Choice2Of2 (
        TruncatedMessage (
          sprintf "cipher text length was %d but expected >= %d"
                  cipher_text.Length (HMACLength + IVLength)))
    elif not (Bytes.cnst_time_cmp hmac_calc hmac_given) then
      Choice2Of2 (AlteredOrCorruptMessage "calculated HMAC does not match expected/given")
    else
      let iv = Array.zeroCreate<byte> IVLength
      Array.blit cipher_text 0
                 iv 0
                 IVLength
      use aes     = secretbox_init key iv
      use denc    = aes.CreateDecryptor(key, iv)
      use plain   = new MemoryStream()
      use crypto  = new CryptoStream(plain, denc, CryptoStreamMode.Write)
      crypto.Write(cipher_text, IVLength, cipher_text.Length - IVLength - HMACLength)
      crypto.FlushFinalBlock()
      Choice1Of2 (plain.ToArray() |> Encoding.gzip_decode)

  let secretbox_open' k c =
    secretbox_open k c |> Choice.map UTF8.to_string'
