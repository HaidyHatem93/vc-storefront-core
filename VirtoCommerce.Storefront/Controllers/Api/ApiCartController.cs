﻿using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;
using VirtoCommerce.Storefront.AutoRestClients.OrdersModuleApi;
using VirtoCommerce.Storefront.Domain;
using VirtoCommerce.Storefront.Model;
using VirtoCommerce.Storefront.Model.Cart;
using VirtoCommerce.Storefront.Model.Cart.Services;
using VirtoCommerce.Storefront.Model.Common;
using VirtoCommerce.Storefront.Model.Common.Events;
using VirtoCommerce.Storefront.Model.Common.Exceptions;
using VirtoCommerce.Storefront.Model.Order.Events;
using VirtoCommerce.Storefront.Model.Services;
using VirtoCommerce.Storefront.Model.Subscriptions;
using VirtoCommerce.Storefront.Model.Subscriptions.Services;
using VirtoCommerce.Storefront.Models;
using orderModel = VirtoCommerce.Storefront.AutoRestClients.OrdersModuleApi.Models;

namespace VirtoCommerce.Storefront.Controllers.Api
{

    public class ApiCartController : StorefrontControllerBase
    {
        private readonly ICartBuilder _cartBuilder;
        private readonly IOrderModule _orderApi;
        private readonly ICatalogService _catalogService;
        private readonly IEventPublisher _publisher;
        private readonly ISubscriptionService _subscriptionService;

        public ApiCartController(IWorkContextAccessor workContextAccessor, ICatalogService catalogService, ICartBuilder cartBuilder,
                                 IOrderModule orderApi, IStorefrontUrlBuilder urlBuilder,
                                 IEventPublisher publisher, ISubscriptionService subscriptionService)
            : base(workContextAccessor, urlBuilder)
        {
            _cartBuilder = cartBuilder;
            _orderApi = orderApi;
            _catalogService = catalogService;
            _publisher = publisher;
            _subscriptionService = subscriptionService;
        }

        // Get current user shopping cart
        // GET: storefrontapi/cart
        [HttpGet]
        public async Task<ActionResult> GetCart()
        {
            var cartBuilder = await LoadOrCreateCartAsync();
            await cartBuilder.ValidateAsync();
            return Json(cartBuilder.Cart);
        }

        // PUT: storefrontapi/cart/comment
        [HttpPut]
        public async Task<ActionResult> UpdateCartComment([FromBody] string comment)
        {
            EnsureCartExists();

            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();

                await cartBuilder.UpdateCartComment(comment);
                await cartBuilder.SaveAsync();
            }

            return Ok();
        }

        // GET: storefrontapi/cart/itemscount
        [HttpGet]
        public async Task<ActionResult> GetCartItemsCount()
        {
            EnsureCartExists();

            var cartBuilder = await LoadOrCreateCartAsync();

            return Json(cartBuilder.Cart.ItemsQuantity);
        }

        // POST: storefrontapi/cart/items
        [HttpPost]
        public async Task<ActionResult> AddItemToCart([FromBody] AddCartItem cartItem)
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();

                var products = await _catalogService.GetProductsAsync(new[] { cartItem.ProductId }, Model.Catalog.ItemResponseGroup.Inventory | Model.Catalog.ItemResponseGroup.ItemWithPrices );
                if (products != null && products.Any())
                {
                    await cartBuilder.AddItemAsync(products.First(), cartItem.Quantity);
                    await cartBuilder.SaveAsync();
                }
                return Json(new { ItemsCount = cartBuilder.Cart.ItemsQuantity });
            }
        }

        // PUT: storefrontapi/cart/items/price
        [HttpPut]
        public async Task<ActionResult> ChangeCartItemPrice([FromBody] ChangeCartItemPrice newPrice)
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();

                var lineItem = cartBuilder.Cart.Items.FirstOrDefault(x => x.Id == newPrice.LineItemId);
                if (lineItem != null)
                {
                    var newPriceMoney = new Money(newPrice.NewPrice, cartBuilder.Cart.Currency);
                    //do not allow to set less price via this API call
                    if (lineItem.ListPrice < newPriceMoney)
                    {
                        lineItem.ListPrice = newPriceMoney;
                    }
                    if (lineItem.SalePrice < newPriceMoney)
                    {
                        lineItem.SalePrice = newPriceMoney;
                    }
                }
                await cartBuilder.SaveAsync();

            }
            return Ok();
        }

        // PUT: storefrontapi/cart/items?lineItemId=...&quantity=...
        [HttpPut]
        public async Task<ActionResult> ChangeCartItem([FromBody] ChangeCartItemQty changeQty)
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();

                var lineItem = cartBuilder.Cart.Items.FirstOrDefault(i => i.Id == changeQty.LineItemId);
                if (lineItem != null)
                {
                    await cartBuilder.ChangeItemQuantityAsync(changeQty.LineItemId, changeQty.Quantity);
                    await cartBuilder.SaveAsync();
                }
            }
            return Ok();
        }

        // DELETE: storefrontapi/cart/items?lineItemId=...
        [HttpDelete]
        public async Task<ActionResult> RemoveCartItem(string lineItemId)
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();
                await cartBuilder.RemoveItemAsync(lineItemId);
                await cartBuilder.SaveAsync();
                return Json(new { ItemsCount = cartBuilder.Cart.ItemsQuantity });
            }

        }

        // POST: storefrontapi/cart/clear
        [HttpPost]
        public async Task<ActionResult> ClearCart()
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();
                await cartBuilder.ClearAsync();
                await cartBuilder.SaveAsync();
            }
            return Ok();
        }

        // GET: storefrontapi/cart/shipments/{shipmentId}/shippingmethods
        [HttpGet]
        public async Task<ActionResult> GetCartShipmentAvailShippingMethods(string shipmentId)
        {
            EnsureCartExists();

            var cartBuilder = await LoadOrCreateCartAsync();

            var shippingMethods = await cartBuilder.GetAvailableShippingMethodsAsync();
            return Json(shippingMethods);
        }

        // GET: storefrontapi/cart/paymentmethods
        [HttpGet]
        public async Task<ActionResult> GetCartAvailPaymentMethods()
        {
            EnsureCartExists();

            var cartBuilder = await LoadOrCreateCartAsync();

            var paymentMethods = await cartBuilder.GetAvailablePaymentMethodsAsync();
            return Json(paymentMethods);
        }

        // POST: storefrontapi/cart/coupons/{couponCode}
        [HttpPost]
        public async Task<ActionResult> AddCartCoupon(string couponCode)
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();

                await cartBuilder.AddCouponAsync(couponCode);
                await cartBuilder.SaveAsync();
                return Json(cartBuilder.Cart.Coupon);
            }
        }


        // DELETE: storefrontapi/cart/coupons
        [HttpDelete]
        public async Task<ActionResult> RemoveCartCoupon()
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();
                await cartBuilder.RemoveCouponAsync();
                await cartBuilder.SaveAsync();
            }

            return Ok();
        }


        // POST: storefrontapi/cart/paymentPlan    
        [HttpPost]
        public async Task<ActionResult> AddOrUpdateCartPaymentPlan([FromBody] PaymentPlan paymentPlan)
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();
                paymentPlan.Id = cartBuilder.Cart.Id;
           
                await _subscriptionService.UpdatePaymentPlanAsync(paymentPlan);
                // await _cartBuilder.SaveAsync();
                cartBuilder.Cart.PaymentPlan = paymentPlan;
            }
            return Ok();
        }

        // DELETE: storefrontapi/cart/paymentPlan    
        [HttpDelete]
        public async Task<ActionResult> DeleteCartPaymentPlan()
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();
                await _subscriptionService.DeletePlansByIdsAsync(new[] { cartBuilder.Cart.Id });
                // await _cartBuilder.SaveAsync();
                cartBuilder.Cart.PaymentPlan = null;
            }
            return Ok();
        }


        // POST: storefrontapi/cart/shipments    
        [HttpPost]
        public async Task<ActionResult> AddOrUpdateCartShipment([FromBody] Shipment shipment)
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();
                await cartBuilder.AddOrUpdateShipmentAsync(shipment);
                await cartBuilder.SaveAsync();
            }

            return Ok();
        }

        // POST: storefrontapi/cart/payments
        [HttpPost]
        public async Task<ActionResult> AddOrUpdateCartPayment([FromBody] Payment payment)
        {
            EnsureCartExists();

            if (payment.Amount.Amount == decimal.Zero)
            {
                return  BadRequest("Valid payment amount is required");
            }

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();
                await cartBuilder.AddOrUpdatePaymentAsync(payment);
                await cartBuilder.SaveAsync();
            }
            return Ok();
        }

        // POST: storefrontapi/cart/createorder
        [HttpPost]
        public async Task<ActionResult> CreateOrder([FromBody] orderModel.BankCardInfo bankCardInfo)
        {
            EnsureCartExists();

            //Need lock to prevent concurrent access to same cart
            using (await AsyncLock.GetLockByKey(GetAsyncLockCartKey(WorkContext.CurrentCart.Value)).LockAsync())
            {
                var cartBuilder = await LoadOrCreateCartAsync();

                var order = await _orderApi.CreateOrderFromCartAsync(cartBuilder.Cart.Id);

                //Raise domain event
                await _publisher.Publish(new OrderPlacedEvent(WorkContext, order.ToCustomerOrder(WorkContext.AllCurrencies, WorkContext.CurrentLanguage), cartBuilder.Cart));


                orderModel.ProcessPaymentResult processingResult = null;
                var incomingPayment = order.InPayments != null ? order.InPayments.FirstOrDefault() : null;
                if (incomingPayment != null)
                {
                    processingResult = await _orderApi.ProcessOrderPaymentsAsync(order.Id, incomingPayment.Id, bankCardInfo);
                }

                await cartBuilder.RemoveCartAsync();

                return Json(new { order, orderProcessingResult = processingResult, paymentMethod = incomingPayment != null ? incomingPayment.PaymentMethod : null });
            }
        }


        private static string GetAsyncLockCartKey(ShoppingCart cart)
        {
            return string.Join(":", "Cart", cart.Id, cart.Name, cart.CustomerId);
        }

        private void EnsureCartExists()
        {
            if (WorkContext.CurrentCart.Value == null)
            {
                throw new StorefrontException("Cart not found");
            }
        }
        private async Task<ICartBuilder> LoadOrCreateCartAsync()
        {
            var cart = WorkContext.CurrentCart.Value;
            //Need to try load fresh cart from cache or service to prevent parallel update conflict
            //because WorkContext.CurrentCart may contains old cart
            await _cartBuilder.LoadOrCreateNewTransientCartAsync(cart.Name, WorkContext.CurrentStore, cart.Customer, cart.Language, cart.Currency);
            return _cartBuilder;
        }
    }
}
